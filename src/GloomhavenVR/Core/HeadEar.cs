using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HEAD EAR — the one AudioListener, owned by however many features need it, destroyed when the
//  last of them lets go. Extracted from Core/EnvSound.cs at ModBuild 297 because a SECOND feature
//  (spatial voice chat) now depends on the ear being on the head, and the first one owned it
//  privately and on its own schedule.
// =================================================================================================

/// <summary>
/// Puts an <see cref="AudioListener"/> on the mod's head camera and keeps it there for as long as
/// at least one named claimant wants it, disabling every other enabled listener while it holds and
/// restoring exactly those on release.
///
/// <para><b>WHY THIS FILE EXISTS — AND IT IS A DEFECT REPORT, NOT A TIDY-UP.</b> Every word of the
/// argument for putting the ear on the head is in <c>Core/EnvSound.cs</c>'s class doc ("THE
/// LISTENER, AND THE FINDING THAT HAD TO BE FIXED BEFORE ANY OF THIS COULD WORK"), and that
/// argument is unchanged. What changed is WHO NEEDS IT. Until now the ear was a private
/// implementation detail of the environment ambience: <c>EnvSound.TakeListener</c> ran from
/// <c>Build()</c> and <c>ReleaseListener</c> from <c>StandDown()</c>, so the mod owned the ear
/// <b>only while an environment was standing with environment sounds switched on</b>. Nothing else
/// in the mod ever creates one — <c>WorldUI/UiSoundEar.OurEar()</c> is a
/// <c>head.GetComponent&lt;AudioListener&gt;()</c> and deliberately never an <c>AddComponent</c>
/// (UiSoundEar.cs:245-261, and its own doc says so: "EnvSound.TakeListener puts exactly one listener
/// on Rig.VRRigDriver.HeadCamera").</para>
///
/// <para>So with environment sounds off — a switch the user is explicitly offered — the enabled
/// listener is whatever the SCENE authored, which is the game camera the mod deliberately keeps
/// alive but PARKED at a frozen orbit pose many world units from the head
/// (<c>Rig/CameraControllerPatches.cs:30-34, 48-52</c>). A spatialised source placed at a peer's
/// mask would then be heard from a fixed point somewhere off in the diorama: attenuated by a
/// distance that has nothing to do with where the player is standing, and panned by a bearing that
/// has nothing to do with where the player is looking. That is not a degraded feature, it is a
/// wrong one, and it would have been invisible to every gate in this repo.</para>
///
/// <para><b>THE CONTRACT.</b> <see cref="Claim"/> and <see cref="Release"/> are idempotent and
/// keyed by a caller-chosen name. The listener is created on the first claim and destroyed on the
/// last release; a claim while already held is free and does NOT re-run the suppression sweep. The
/// names are not decoration — they are what the log line says when the ear changes hands, so a
/// reader can tell "the environment stood down" from "voice chat disconnected" without guessing.
/// </para>
///
/// <para><b>WHY THE SUPPRESSION LIST IS TAKEN ONCE AND NOT RE-SWEPT PER FRAME.</b> Unity permits
/// exactly one enabled <see cref="AudioListener"/>; more than one is undefined behaviour and logs a
/// warning every frame. A listener that gets enabled AFTER we take ownership would therefore break
/// the panning and we would not notice. The obvious remedy — an
/// <c>Object.FindObjectsOfType&lt;AudioListener&gt;()</c> on a cadence — is a whole-scene sweep, and
/// this project has already lost an entire frame budget to exactly one of those per tick
/// (<c>Net/NetAvatarDriver.cs:604-618</c>). It is not taken, because the only game code that would
/// ever enable a second listener is <c>GH.Runtime/VoiceChat/BoltVoicePlayerController.cs:18</c>
/// (<c>GetComponent&lt;AudioListener&gt;().enabled = entity.IsOwner</c>) and <b>that class is dead
/// code in this game</b>: <c>IVoicePlayer</c> has no definition anywhere in the decompiled tree,
/// there is no <c>BoltPrefabs.cs</c>, and nothing references the type. EnvSound's own comment at
/// :4630-4632 asserts that prefab is live in multiplayer; that assertion is WRONG and this is the
/// correction. If a second-client test ever shows a "There are N audio listeners in the scene"
/// warning, THIS paragraph is the thing that was wrong and a cadenced re-sweep is the fix.</para>
///
/// <para><b>NOT VERIFIED ON HARDWARE.</b> Everything above is read off source. The one behaviour a
/// desk cannot confirm is what the scene actually contains at the moment of the first claim in a
/// live multiplayer session. See <c>.planning/VOICE-SPATIAL.md</c>.</para>
/// </summary>
internal static class HeadEar
{
    private const string Scope = "Core";

    /// <summary>Who currently wants the ear. Empty ⇒ the mod does not own it.</summary>
    private static readonly HashSet<string> Claims = new();

    /// <summary>The listener this class created on the head camera, or null when unowned.</summary>
    private static AudioListener? _ours;

    /// <summary>
    /// The listeners that were enabled when we took over, in the order we found them, so
    /// <see cref="Release"/> can restore precisely those and nothing else. Unity fake-null covers
    /// the case where the scene that owned one has since unloaded.
    /// </summary>
    private static readonly List<AudioListener> Suppressed = new();

    /// <summary>
    /// The mod's ear when the mod owns one AND it is enabled, else null. This is the same question
    /// <c>WorldUI/UiSoundEar.OurEar()</c> asks by <c>GetComponent</c>; that path is left exactly as
    /// it was, because it must keep answering correctly even if this class is never claimed.
    /// </summary>
    internal static AudioListener? Current => _ours != null && _ours.enabled ? _ours : null;

    /// <summary>True while <paramref name="who"/> holds a claim.</summary>
    internal static bool Holds(string who) => Claims.Contains(who);

    /// <summary>True while anybody holds a claim.</summary>
    internal static bool Owned => Claims.Count > 0;

    /// <summary>How many listeners were disabled to take the ear — reported in EnvSound's dump.</summary>
    internal static int SuppressedCount => Suppressed.Count;

    /// <summary>
    /// Take the ear on <paramref name="who"/>'s behalf, or note that they also want it. Returns
    /// true when the mod owns an enabled listener after the call.
    ///
    /// <para>Safe to call every frame: the fast path when the ear is already ours is one
    /// <see cref="HashSet{T}.Contains"/>.</para>
    /// </summary>
    internal static bool Claim(string who)
    {
        // ALREADY OURS ⇒ record the claim and stop. This guard is load-bearing rather than
        // defensive, and the reason is inherited verbatim from EnvSound.TakeListener: a style
        // change tears the old environment down and builds the new one IN THE SAME FRAME, and if
        // that teardown destroyed the listener this method would then find the doomed component
        // with GetComponent (Object.Destroy is deferred to the end of the frame), re-enable it, and
        // Unity would delete it moments later — leaving the session with NO enabled listener at all.
        // Keeping ownership across a rebuild also keeps Suppressed intact, which is the ONLY record
        // of which listeners have to be handed back.
        if (_ours != null)
        {
            Claims.Add(who);
            return _ours.enabled;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
        {
            // No head camera yet (early boot, or VR down). Do NOT record the claim — a claimant
            // that asked before the rig existed must ask again, or the first successful Claim would
            // be skipped by the fast path above while Claims looked satisfied.
            return false;
        }

        Suppressed.Clear();
        foreach (AudioListener l in Object.FindObjectsOfType<AudioListener>())
        {
            if (l == null || !l.enabled)
                continue;
            l.enabled = false;
            Suppressed.Add(l);
        }

        _ours = head.gameObject.GetComponent<AudioListener>();
        bool created = _ours == null;
        if (created)
            _ours = head.gameObject.AddComponent<AudioListener>();
        _ours!.enabled = true;

        Claims.Add(who);

        VRLog.Info(Scope, $"HEAD EAR taken by '{who}' — the AudioListener is {(created ? "now" : "already")} on " +
                          $"'{head.gameObject.name}' and {Suppressed.Count} pre-existing enabled listener(s) were " +
                          "disabled and remembered for restore. Unity permits exactly one enabled listener; every " +
                          "spatialised source in the mod (environment ambience, and now peer voices) is heard from " +
                          "this point. The ear is handed back when the LAST claimant releases it.");
        return true;
    }

    /// <summary>
    /// Drop <paramref name="who"/>'s claim. The listener survives while anybody else still wants
    /// it; on the last release it is destroyed and every suppressed listener is re-enabled.
    /// Idempotent — releasing a claim that was never taken is free.
    /// </summary>
    internal static void Release(string who)
    {
        if (!Claims.Remove(who))
            return;

        if (Claims.Count > 0)
        {
            VRLog.Info(Scope, $"HEAD EAR: '{who}' released it, but {Claims.Count} claimant(s) still hold it " +
                              $"({string.Join(", ", Claims)}) — the listener stays on the head.");
            return;
        }

        if (_ours != null)
        {
            Object.Destroy(_ours);
            _ours = null;
        }

        int restored = 0;
        for (int i = 0; i < Suppressed.Count; i++)
        {
            AudioListener l = Suppressed[i];
            // Unity fake-null when the scene that owned it unloaded; then there is nothing to
            // restore and the scene took the state with it.
            if (l != null)
            {
                l.enabled = true;
                restored++;
            }
        }
        Suppressed.Clear();

        VRLog.Info(Scope, $"HEAD EAR handed back — '{who}' was the last claimant, the mod's listener is destroyed " +
                          $"and {restored} pre-existing listener(s) re-enabled. Anything the mod spatialises from " +
                          "here on is heard from wherever the game's own listener sits, which is a PARKED camera; " +
                          "that is why every spatial feature claims the ear for as long as it is running.");
    }
}

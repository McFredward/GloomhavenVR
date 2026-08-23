using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE ONE FACT TWO SUBSYSTEMS HAVE TO AGREE ON: is the game's multiplayer ready toggle
/// ('Multiplayer Ready Toggle', a 307x65 <c>UIWindow</c> carrying <c>UIReadyToggle</c>) currently
/// PARKED inside somebody else's floated window?
///
/// <para>WHY THIS EXISTS AS ITS OWN FILE. Two rules meet on this object and each is right on its
/// own:</para>
///
/// <list type="number">
/// <item>A bare confirm button must never float as a window of its own. ModBuild 231's hardware
/// log shows the catch-all doing exactly that — <c>CATCH-ALL: unknown scenario window
/// 'Multiplayer Ready Toggle' (ID None) floated — enroll it explicitly</c> (Player.log:18129),
/// followed by <c>MODAL CLOSE (X button): attached to 'Multiplayer Ready Toggle'</c> — and the
/// user's screenshot (<c>frei_schwebender_button_multiplayer.jpg</c>) is a 'Quest wählen' button
/// hanging in the forest inside a frame with a close cross. Verbatim: "Wieso auch immer ist kurz
/// dannach dann auch ein frei schwebender Button erschienen in einem eigenen Fenster … Das darf
/// nicht sein."</item>
/// <item>A window must never be invisible. That rule is older and it outranks the first one: a
/// confirm the player cannot reach is a DEADLOCK, and this round already contains two of those.
/// Refusing the float unconditionally would trade a cosmetic fault for a fatal one.</item>
/// </list>
///
/// <para>So the refusal is CONDITIONAL on somebody having taken responsibility for showing the
/// button somewhere better, and this class is where that responsibility is recorded.
/// <c>MapTravelConfirm</c> — which has parked the confirm under the quest information since
/// ModBuild 190 and knows about the online variant since ModBuild 226 — is the writer.
/// <c>ModalFallback</c>'s catch-all is the reader. Neither of them may reach into the other, and
/// neither of them owns the fact, which is why it lives here rather than as a property on one of
/// them.</para>
///
/// <para>THE CLAIM IS LEVEL-TRIGGERED AND IT MUST BE RE-ASSERTED. <see cref="Set"/> is called from
/// the parker's own reconcile tick with the live answer every time it runs; the claim is not a
/// latch. If the parker stands down, throws, or simply stops running, <see cref="Claimed"/> goes
/// false again within one tick and the button floats on its own — ugly, and reachable, which is
/// the correct trade. <see cref="Reset"/> exists for the room standing down.</para>
///
/// <para>NOTHING HERE IS REPLICATED and nothing here is game state. It records where a local uGUI
/// subtree is being drawn on THIS client. Two players may legitimately disagree about it.</para>
/// </summary>
internal static class ReadyToggleParkClaim
{
    /// <summary>
    /// How long a claim stays good without being re-asserted, seconds. The parker re-asserts from
    /// its own reconcile tick, so this only has to outlive one skipped tick — a frame hitch, a
    /// guarded throw, a scene load — and not a stand-down. Deliberately shorter than the
    /// catch-all's own float grace, so the refusal lapses BEFORE the player has had time to look
    /// for the button and not find it.
    /// </summary>
    private const float ClaimLifetimeSeconds = 1.0f;

    private static float _claimedUntil;
    private static string _why = "never claimed";
    private static GameObject? _claimedObject;

    /// <summary>
    /// Is the ready toggle being presented somewhere else right now? False whenever the claim has
    /// not been re-asserted inside <see cref="ClaimLifetimeSeconds"/> — see the class doc for why
    /// the lapse is the safe direction.
    /// </summary>
    internal static bool Claimed => Time.unscaledTime < _claimedUntil;

    /// <summary>The parker's own words for what it did, for the log line the reader prints when it
    /// refuses a float. Never empty.</summary>
    internal static string Why => _why;

    /// <summary>The GameObject the claim is about, so a reader can check it is talking about the
    /// same toggle instance rather than trusting a name. Null when nothing is claimed.</summary>
    internal static GameObject? ClaimedObject => Claimed ? _claimedObject : null;

    /// <summary>
    /// Re-assert (or drop) the claim. Called every parker tick with the live answer — see the
    /// class doc: this is a level, not an edge.
    /// </summary>
    /// <param name="target">The ready toggle's own GameObject, or null to drop the claim.</param>
    /// <param name="why">One sentence naming where the button is being drawn instead. Shown in the
    /// reader's refusal line, so it has to read as an explanation and not as a status code.</param>
    internal static void Set(GameObject? target, string why)
    {
        if (target == null)
        {
            _claimedUntil = 0f;
            _claimedObject = null;
            _why = string.IsNullOrEmpty(why) ? "the parker dropped its claim" : why;
            return;
        }

        _claimedObject = target;
        _claimedUntil = Time.unscaledTime + ClaimLifetimeSeconds;
        _why = string.IsNullOrEmpty(why) ? "parked by MapTravelConfirm" : why;
    }

    /// <summary>Forget everything — the room stood down, so no claim can be in force.</summary>
    internal static void Reset()
    {
        _claimedUntil = 0f;
        _claimedObject = null;
        _why = "the map room stood down";
    }
}

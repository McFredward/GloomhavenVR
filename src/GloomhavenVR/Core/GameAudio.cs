namespace GloomhavenVR.Core;

/// <summary>
/// The mod's ONE way to make the GAME's own audio system say something in VR.
///
/// ───────────────────────────────────────────────────────────────── WHY THIS EXISTS AT ALL ──
///
/// This is not a convenience wrapper. It is the hard-won recipe from the card-fan sound bug
/// (hardware log build 07621c087: "sound on close but never on open" — zero sound lines, close
/// audible, open silent), lifted out of <c>Cards/CardsDriver.2.Update.cs</c> so the SECOND
/// caller (the initiative-track refusal sound, user report 2026-08-09) cannot re-learn it the
/// expensive way (that caller is now
/// <c>WorldUI.Surfaces.InitiativePortraitClickSound</c>, which plays both the refusal and the
/// character-change item through this one shape). The trap, verbatim from that fix:
///
/// <para>The mod NEVER touches the <c>AudioListener</c> — it rides the GAME's 2D camera, not the
/// VR head or hands. So <c>AudioController.Play(item, someTransform, …)</c>, the POSITIONAL
/// overload, plays a 3D-configured item at a transform that is metres away from where the
/// listener actually is, and it attenuates to nothing — SILENTLY, because
/// <c>AudioController.Play</c> "succeeds" and returns a live <c>AudioObject</c>. A 2D item
/// ignores position and stays audible, which is exactly why the bug looked like "one edge
/// works, the other does not" instead of like a listener problem.</para>
///
/// <para>The fix, and the only shape this class offers: play LISTENER-ANCHORED via
/// <c>AudioController.Play(item)</c> (listener position + forward). That is the exact call the
/// game itself makes for every UI sound — <c>AudioControllerUtils.PlaySound</c> is literally
/// <c>if (AudioController.IsValidAudioID(id)) return AudioController.Play(id);</c> (decompiled
/// GH.Runtime/AudioControllerUtils.cs:16-26) — so it is immune to any rig/listener placement.
/// The positional overload still has ONE legitimate user, <c>CardsDriver.PlayCardSound</c>, which
/// deliberately mirrors <c>FullAbilityCard.cs:590</c> for card-UI sounds that the game itself
/// positions; that call site keeps its own copy and its own doc, and this class is not it.</para>
///
/// <para>VALIDATION FIRST, ALWAYS. <c>AudioController.Play</c> on an unknown id logs a Unity
/// error that BepInEx does not capture here, so every id is checked with
/// <c>IsValidAudioID</c> before it is played and the caller is TOLD which one survived — that is
/// what makes the next hardware log decisive about which sound was chosen instead of leaving it
/// to be guessed from memory.</para>
/// </summary>
internal static class GameAudio
{
    /// <summary>
    /// Play the first VALID audio id out of <paramref name="preferred"/> then
    /// <paramref name="fallbacks"/>, LISTENER-ANCHORED (see the class doc — the positional
    /// overload is silent in VR for a 3D item).
    ///
    /// <para>Returns true when <c>AudioController.Play</c> handed back an <c>AudioObject</c>;
    /// false means either nothing resolved (<paramref name="valid"/> false) or the controller
    /// refused the play — audio disabled, or the item's own
    /// <c>MinTimeBetweenPlayCalls</c> throttle. The three out-parameters exist so the CALLER
    /// writes its own proof line in its own vocabulary: <paramref name="item"/> is the id that
    /// was actually played (or the one that failed), <paramref name="valid"/> is
    /// <c>IsValidAudioID</c> at call time, and <paramref name="note"/> carries the fallback /
    /// exception explanation ready to append to a log line.</para>
    ///
    /// <para>Never throws: a not-yet-alive audio controller (main menu, scene load) or a
    /// half-initialised item table must degrade to "no sound", never break the interaction path
    /// that asked for the sound.</para>
    /// </summary>
    internal static bool PlayListenerAnchored(string? preferred, string[]? fallbacks,
                                              out string item, out bool valid, out string note)
    {
        string configured = preferred ?? string.Empty;
        item = configured;
        valid = false;
        note = string.Empty;
        bool played = false;
        try
        {
            valid = item.Length > 0 && AudioController.IsValidAudioID(item);
            if (!valid && fallbacks != null)
            {
                for (int i = 0; i < fallbacks.Length; i++)
                {
                    if (string.IsNullOrEmpty(fallbacks[i]) || !AudioController.IsValidAudioID(fallbacks[i]))
                        continue;
                    item = fallbacks[i];
                    valid = true;
                    note = $" — configured '{configured}' empty/unknown, fell back to verified game item";
                    break;
                }
            }
            if (valid)
                played = AudioController.Play(item) != null;
        }
        catch (System.Exception ex)
        {
            note = $" — threw {ex.GetType().Name}: {ex.Message}";
        }
        return played;
    }
}

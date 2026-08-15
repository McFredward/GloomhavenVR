using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HAUNT FIGURES — THE THREE PURE FUNCTIONS. How dark a figure is, when it materialises, and how
//  hot its burn edge is allowed to glow.
//
//  WHY THIS FILE IS SEPARATE AND WHY IT CONTAINS NOTHING BUT ARITHMETIC. It is compiled into
//  tests/GloomhavenVR.WireTests verbatim (see that project's .csproj), so these three functions are
//  the ones the vectors drive — not a copy of them. That is the whole reason the file touches
//  nothing but Mathf: an apparition's darkness and its materialise window are observed only by FEEL,
//  from inside a headset, one photograph per round, and four rounds have already been spent on a
//  constant nobody could test. A mirrored re-implementation in the test would have passed happily
//  through every one of those four.
// =================================================================================================

internal static partial class HauntFigures
{
    // ---- THE DARKENING, re-fitted in ModBuild 153 off the FIRST photographs in which the lever
    // ---- actually reaches a pixel -----------------------------------------------------------------
    //
    // WHAT CHANGED UNDER THESE FOUR NUMBERS. Through ModBuild 152 the multiplier was written into
    // the material colour property _MOD_TINT, and Amp_Char_Shader ignores it: the ModBuild 152 log
    // (LogOutput.log:1585) reports that shader as `lit=NO (no ForwardBase pass)` and dumps
    // _MOD_TINT=(1.000,1.000,1.000,0.000) live off the rendering material while the figure rendered
    // at full albedo. So EVERY earlier fit of DarkFloor/LightGain/MaxLevel was fitted through an
    // inert lever, and ModBuild 152 wrote its own escape hatch for exactly this outcome:
    //
    //     "IF THE PROPERTY LINES BELOW SHOW a=1.000 WITH A NEAR-BLACK RGB AND THE FIGURE IS STILL
    //      FULLY LIT, the alpha was not the gate and the next round must change LEVER (the albedo
    //      texture) rather than tune the RGB a fifth time."
    //
    // The hatch fired. The lever is now the albedo TEXTURE (see HauntFigures.Albedo.cs), and these
    // four are re-fitted from scratch against the three photographs of ModBuild 152 rather than
    // carried over. None of the previous values survives.
    //
    // THE MEASUREMENT. Rec.709 luma on the gamma-encoded bytes of the user's three 3840x2160
    // photographs, figure separated from surround by a luminance mask over a hand-placed box (the
    // mask was rendered back out and looked at, so the instrument is known to be pointed at the
    // creature and not at the wall behind it):
    //
    //     Figur_hell7.jpg  cellar, skeleton in the stair shaft
    //         figure p50 0.1630  p90 0.3870 | masonry p50 0.0457 | black stair shaft p50 0.0059
    //     Figur_hell8.jpg  cellar, skeleton behind the window slot
    //         figure p50 0.1605  p90 0.3386 | masonry p50 0.0216 | lit alcove behind it p50 0.0650
    //     Figur_hell9.jpg  wood, cultist between the trunks
    //         figure p50 0.0966  p90 0.2257 | forest immediately around it p50 0.0084
    //
    // THE TARGET, from the user's own sentence — "in der Dunkelheit nur als Siluetten Erkennbar".
    // A silhouette is a shape that is FOUND but not READ. Placing the figure's own p50 at 0.019 and
    // its p90 at 0.045 does that: 0.045 is the luminance of the cellar's lit masonry, so the
    // creature's brightest bone lands at the brightness of an ordinary wall stone and everything
    // else lands below it. Face, ribs and sash stop being legible while the outline survives.
    //
    // Both criteria are solved independently per photograph and they AGREE, which is what makes the
    // fit a fit rather than a preference — p50 0.019 wants 0.123 / 0.125 / 0.207 and p90 0.045 wants
    // 0.116 / 0.133 / 0.199, i.e. cellar 0.12 and wood 0.20 under either reading.
    //
    // THE LINE THROUGH THE TWO ROOMS. Room luminance is the rig figure the lighting side already
    // measures at the creature's chest: cellar 0.0425, wood 0.2126.
    //     LightGain = (0.1999 - 0.1200) / (0.2126 - 0.0425) = 0.470
    //     DarkFloor =  0.1200 - 0.470 x 0.0425             = 0.100
    // giving cellar 0.1200 and wood 0.1999, a room ratio of 1.666. ONE rule couples the two rooms;
    // there is no second constant that can drift away from the first.
    //
    // WHAT THE PHOTOGRAPHS THEN PREDICT, and this is the number the next round checks:
    //     hell7  figure p50 0.0196 against the black stair shaft 0.0059  = 3.3x  (light on dark)
    //     hell8  figure p50 0.0193 against the lit alcove       0.0650  = 0.30x (dark on light)
    //     hell9  figure p50 0.0193 against the forest           0.0084  = 2.3x  (light on dark)
    // Two of the three read as a pale shape in a black opening and one as a dark shape against a
    // lighter alcove. Both are silhouettes, which is why one rule is allowed to produce both.
    //
    // WHICH KNOB A TUNING DROP MOVES:
    //   * "immer noch zu hell"        -> LightGain down. It is the whole slope between the rooms.
    //   * "ich finde sie gar nicht"   -> DarkFloor up. It is what the darker room is made of.
    //   * a figure that walks into a candle pool going bright -> MaxLevel down.

    /// <summary>A creature is never fully black while it is present. At the cellar's rig luminance
    /// this constant IS almost the whole answer (0.100 of 0.120), because the cellar's own rig
    /// delivers nearly nothing and what the figure is really lit by is the game's FireTorch point
    /// light a few metres away.</summary>
    private const float DarkFloor = 0.100f;

    /// <summary>Room luminance to albedo multiplier — the slope through the two rooms.</summary>
    private const float LightGain = 0.470f;

    /// <summary>Never at full albedo: it is a thing in the dark. 0.28 is 1.4x the wood's own answer,
    /// so it bites only where it should — on a figure standing in light neither room has.</summary>
    private const float MaxLevel = 0.28f;

    /// <summary>The rig could not be read at all. Deliberately just UNDER the darker room's answer
    /// (0.120): a figure whose room could not be measured is still a figure in the dark, and the one
    /// value this must never fall back to is full albedo.</summary>
    private const float UnlitLevel = 0.11f;

    /// <summary>
    /// The albedo multiplier the room's measured light justifies, 0..1. See THE DARKENING above for
    /// the three photographs it is fitted to and for which knob a tuning drop moves.
    /// </summary>
    internal static float RoomLevel(float roomLuminance)
    {
        if (float.IsNaN(roomLuminance) || roomLuminance < 0f)
            return UnlitLevel;
        return Mathf.Clamp(DarkFloor + LightGain * roomLuminance, DarkFloor, MaxLevel);
    }

    /// <summary>The value <see cref="RoomLevel"/> returns when no rig could be measured. Exposed so
    /// the vectors can assert the fallback is BELOW the darker room's answer rather than above it —
    /// the direction of that inequality is the difference between "dim" and "fully lit".</summary>
    internal static float UnlitRoomLevel => UnlitLevel;

    // ---- THE BURN EDGE ---------------------------------------------------------------------------
    //
    // _CindersGlow is an EMISSIVE term the character shader ADDS after lighting, so no albedo lever
    // can reach it — and at its authored 2.0 it is why ModBuild 152 switched the dissolve hard off
    // ("its emissive burn edge was the 'schwarze Flecken' and half of the 'voll angestrahlt'
    // report"). The dissolve is coming back (see DissolveCutout), so the edge needs a number rather
    // than a switch.
    //
    // THE ARITHMETIC. _CindersColour is authored (1.000, 0.443, 0.051); its Rec.709 luma is 0.5331.
    // Taking the edge's rendered luminance as colour-luma x glow:
    //     authored 2.0        -> 1.0662, i.e. 23.6x the figure's own p90 in the wood. That is the
    //                            "voll angestrahlt" complaint arriving through a new door.
    //     CinderShare x Level -> wood   0.85 x 0.1999 = 0.1699 -> 0.0906 = 2.01x figure p90 0.0451
    //                            cellar 0.85 x 0.1200 = 0.1020 -> 0.0544 = 1.17x figure p90 0.0464
    //
    // 0.85 IS CHOSEN AGAINST THE FIGURE, NOT AGAINST THE ROOM: it is the share that puts the wood's
    // burn edge at TWICE the figure's own brightest pixel. Twice is enough for an ember to read as
    // an ember and nowhere near enough for it to outshine the body it is eating — the failure mode
    // is an edge brighter than the creature, and at 2x the creature is still the brightest large
    // thing in frame. Because it rides Level, the cellar's edge comes out at 0.60x the wood's from
    // the same one constant, which is the "a dissolving figure in the cellar glows a fraction of
    // what one in the wood does" requirement discharged by construction rather than by a second
    // number.
    //
    // _CindersColour IS LEFT AT ITS AUTHORED HUE ON PURPOSE. The generic emission loop scales
    // colours AND strengths by the same factor, which would make the edge go as the SQUARE of the
    // room level (0.03 x 0.03 in the cellar) and extinguish it entirely — an invisible burn edge is
    // not a dissolve in particles, it is the hard cut the user has now reported twice. So the
    // cinder trio is exempted from that loop and driven from here instead.

    /// <summary>How bright the cinder edge may be as a share of the room's albedo level. See THE
    /// BURN EDGE.</summary>
    private const float CinderShare = 0.85f;

    /// <summary>The Rec.709 luma of the authored <c>_CindersColour</c> (1.000, 0.443, 0.051). Held
    /// here rather than in a comment because <see cref="CinderShare"/> was chosen against it: if the
    /// shipped colour ever changes, the vectors that pin the edge against the figure fail.</summary>
    internal const float CinderColourLuma = 0.5331f;

    /// <summary>What <c>_CindersGlow</c> is driven to while a figure is materialising or dissolving
    /// in the given room. Zero outside those windows is not this function's job — the dissolve
    /// toggle is off there, so the shader cannot add the term at all.</summary>
    internal static float CinderGlow(float roomLevel) => Mathf.Max(0f, CinderShare * roomLevel);

    // ---- THE MATERIALISE AND THE DISSOLVE --------------------------------------------------------
    //
    // USER REPORT, ModBuild 152, verbatim: "Das ein und ausblenden ist immer noch nicht wirklich
    // erkennbar. ... Ich fänd es cooler wenn sich die figur eher in partikel auflösen bzw
    // materialisieren würde (nur ganz kurz zum auftauchen bzw auflösen)."
    //
    // THE GAME ALREADY HAS THIS AND THIS SIDE DOES NOT HAVE TO BUILD IT. Two of the game's own
    // runtime components drive exactly this effect on exactly this shader family, and both were read
    // from the decompiled sources rather than inferred:
    //
    //   * SummonAppear.OnEnable/Play (decompiled/GH.Runtime/SummonAppear.cs:61-166) MATERIALISES:
    //     it sets _Toggle_Dissolve = 1 and _Cutout = 1 (fully gone), ramps _Cutout DOWN to 0 through
    //     a curve, and then sets _Toggle_Dissolve = 0 so the steady state is an ordinary solid
    //     character.
    //   * DeathDissolve.Play (decompiled/GH.Runtime/DeathDissolve.cs:135-196) DISSOLVES: same
    //     toggle, _Cutout ramped UP from 0, same toggle-off at the end.
    //
    // SO THE DRIVER IS _Cutout AND NOT _DeathDissolvePos. 0 is solid, 1 is gone. That is worth
    // stating flatly because the brief for this round named _DeathDissolvePos, and neither of the
    // game's two drivers ever writes it — the one place in the whole decompile that touches the
    // _DeathDissolve* family is CharacterRevealScript.Instantiation (:34-45), a bespoke
    // character-select scene which rewrites _DeathDissolveTop/_Bottom with a -1.35 offset of its
    // own. Following the two RUNTIME drivers instead means _DeathDissolveTop (2.0) and
    // _DeathDissolveBottom (-0.1) are left exactly as the artists authored them, which makes the
    // object-space-or-world-space question this round was asked to settle MOOT: every monster death
    // in the shipped game sweeps through those same authored values, so whatever space they are in,
    // they are already correct for this mesh. The same argument keeps _Toggle_FlipDissolveDirection
    // and _ToggleDissolveFromCenter at their authored 0 — that is the direction the cinder edge was
    // tuned for, and it is identical in both rooms because it is not a per-room choice at all.
    //
    // ONE CLOCK, NOT TWO. The window is `min(EdgeSeconds, len/3)` — the SAME expression EdgeFade
    // uses for the brightness envelope, evaluated from the same t and the same len. Not "the same
    // number written twice": HauntFigures.Events.cs calls this function and EdgeFade with identical
    // arguments, so a change to EdgeSeconds moves both or neither.
    //
    // MULTIPLAYER. It is a pure function of (t, len). t is the shared environment clock minus the
    // apparition's own start, len is derived from the event. No Time.time, no frame count, no
    // per-client state, no wire traffic — two clients in the same room with the same switches on
    // evaluate the same float. The vectors assert that by evaluating the same instants in two
    // different orders and requiring bit equality.
    //
    // IT CAN NEVER OUTLAST THE APPARITION. `len/3` is the binding term for anything shorter than
    // 1.05 s, so the two windows together take at most two thirds of the run and can never overlap.
    // The cellar window figure is up for 5.50 s and the walking hound for 2.85 s, so in practice
    // both get the full 0.35 s at each end.

    /// <summary>The floor on a figure's appearance and dissolution, in seconds, at each end. Shared
    /// by the brightness envelope (<c>EdgeFade</c>) and by <see cref="DissolveCutout"/>.</summary>
    internal const float EdgeSeconds = 0.35f;

    /// <summary>
    /// The game shader's <c>_Cutout</c> for an apparition that is <paramref name="t"/> seconds into
    /// a run of <paramref name="len"/> seconds: 1 = not there yet / gone, 0 = solid. See THE
    /// MATERIALISE AND THE DISSOLVE.
    /// </summary>
    internal static float DissolveCutout(float t, float len)
    {
        if (float.IsNaN(t) || float.IsNaN(len) || len <= 0f)
            return 1f;
        float e = Mathf.Min(EdgeSeconds, len / 3f);
        if (e <= 1e-4f)
            return 0f;                     // no room for a window: solid rather than never-visible
        if (t <= 0f || t >= len)
            return 1f;                     // outside its own run the figure is gone, not solid

        // Materialise: 1 -> 0 over the first e seconds. Dissolve: 0 -> 1 over the last e.
        // The maximum of the two is safe because e <= len/3 keeps them apart, and it is the term
        // that makes the steady middle EXACTLY 0 rather than nearly 0.
        float appear = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / e));
        float vanish = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - t) / e));
        return Mathf.Clamp01(Mathf.Max(appear, vanish));
    }

    /// <summary>True while <see cref="DissolveCutout"/> is doing anything at all — i.e. while
    /// <c>_Toggle_Dissolve</c> must be 1. Outside this the toggle goes to 0, which is what keeps the
    /// steady state bit-identical to a figure that never dissolved and is why the emissive burn edge
    /// ModBuild 152 removed cannot come back between the windows.</summary>
    internal static bool Dissolving(float t, float len) => DissolveCutout(t, len) > 1e-4f;
}

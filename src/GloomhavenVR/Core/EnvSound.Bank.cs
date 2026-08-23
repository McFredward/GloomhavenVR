using UnityEngine;

// The generator draws below are written `new Rng(...)`, and they stay that way: the struct moved to
// Core/EnvSoundSchedule.cs so the one part of this file with a TERMINATION property could be
// compiled into the wire tests without the Unity audio module, and an alias keeps every call site —
// and therefore every draw sequence, and therefore every clip — exactly as it was.
using Rng = GloomhavenVR.Core.EnvSoundRng;

namespace GloomhavenVR.Core;

/// <summary>
/// The clips <see cref="EnvSoundBank"/> offers, as a NAME rather than a property reference.
///
/// <para>It exists so that <see cref="EnvSound"/>'s emitter table can be written as data — a cue is
/// "card 5 gets <c>Fall</c>", not "card 5 gets whatever this property currently returns" — and so a
/// log line can print WHICH clip a cue chose without a lookup table of its own. Every value maps to
/// exactly one property on the bank; <see cref="EnvSoundBank.Bank"/> is the only translation.</para>
/// </summary>
internal enum EnvSoundClip
{
    /// <summary>THE WIND. The shared stationary noise bed the window <c>Draught</c> and the swamp
    /// <c>Leaves</c> ride, and NOTHING ELSE MAY RIDE IT — every emitter that plays this clip is
    /// hard-gated on the Air element through <c>EnvSound.WindBed</c>, because the user's standing
    /// ruling is that there is no wind SOUND without wind ("Wind Geräusch nur wenn auch Wind aktiv
    /// ist, sonst kein Geräusch"). Until ModBuild 153 the cellar's candle flames played it too, and
    /// were not gated, and got LOUDER with a Fire infusion; see <see cref="Flutter"/>. IT IS NOT THE
    /// FIRE either: see <see cref="Roar"/>.</summary>
    Bed,

    /// <summary>A CANDLE FLAME — narrow-band noise fluttering at ~11 Hz with the wick's sputters in
    /// it. Looped, 7 s. Its own clip since ModBuild 153, because it is neither a draught (it has no
    /// low end at all: 1.1% under 200 Hz against the wind bed's 53.2%) nor a fire's convective roar;
    /// see <c>EnvSoundBank</c>'s THE CANDLE.</summary>
    Flutter,

    // THERE ARE NO ROOM-TONE CLIPS. `Stone` (the cellar's air) and `NightAir` (the wood's) stood
    // here from ModBuild 154 to 222 and are DELETED, with their generators, their constants, their
    // levels and their two dials. USER RULING, 2026-08-22 hardware, verbatim, both sentences:
    //
    //     "2) Im Keller hören sich die Geräusche an wie Rauschen bei nem Fernseher. Es soll
    //      dezenter sein nicht aufdringliuch und auf keinen Fall nervig."
    //
    //     "3) Auch die kontinuierlichen Sounds im Wald nerven mich. Statt generrell durchgehende
    //      sounds zu machen lieber die Tierrufe und im Wald ein ganz leiser dezenter Windzug."
    //
    // That is a rejection of the DESIGN and not of a level: the whole idea of a continuous bed
    // belonging to no object is what he is turning down, and the third sentence of the same report
    // says what to do instead ("Mach die meistens Sounds an eine Quelle in der Welt hörbar"). A
    // room tone has no source by construction — that is what THE ROOM ITSELF's own doc argued for
    // its 180-degree spread — so there is nothing to move it onto and nothing to rescue.
    //
    // AND THE CELLAR'S WAS MEASURABLY TELEVISION STATIC, which is a decided question rather than a
    // matter of taste: .planning/envsound-replica/room.py's static_report() scores every bed on the
    // four things static IS — 1/3-octave spectral flatness, spectral spread, envelope
    // autocorrelation and envelope swing — against band-limited white noise as the control. The
    // ModBuild 222 stone bed was the flattest, widest and most envelope-free thing this bank had
    // ever played AND the loudest thing in the cellar at rest. Its numbers are in EnvSound.cs's
    // THE ROOM TONES, DELETED block, where the levels that replace them are stated beside them.
    //
    // DO NOT RE-DERIVE ONE. If a future round finds a room "too silent", the answer this report
    // gives is more EVENTS from more PLACES, not a bed. The generators are preserved offline in
    // room.py as the before column, so nothing is lost that a measurement would want.

    /// <summary>The convective column of a real fire — the low, breathy rush. Looped, 6 s, and its
    /// own clip rather than <see cref="Bed"/> because a fire PULSES and a draught does not; see
    /// <c>EnvSoundBank.MakeRoar</c>.</summary>
    Roar,

    /// <summary>One crackle: a burst of wood cells bursting. One-shot, 55 ms. VARIANT 0 of four —
    /// see <c>EnvSoundBank.CrackleVariant</c>.</summary>
    Crackle,

    /// <summary>An ember settling in the bed of a fire. One-shot, 220 ms, duller and rarer than the
    /// crackle. VARIANT 0 of two — see <c>EnvSoundBank.EmberVariant</c>.</summary>
    Ember,
    Drip,
    Squeak,
    Skitter,
    Rumble,
    // THERE IS NO `Chirr`. The wood's insect bed was DELETED at ModBuild 226 — it is the sound the
    // user reported as rain ("Im Wald gefällt mir nur dieser 'Regen' Sound nicht der ab und zu kommt
    // und für eine Zeit bleibt"), and the schedule, the rain control and the falsified alternatives
    // are in EnvSound.cs under THE INSECT CHORUS, DELETED. The generator is preserved sample for
    // sample in .planning/envsound-replica/room.py as make_chirr, which is the rule the room tones'
    // deletion established: a deletion whose before-column has been deleted is one nobody can check.

    /// <summary>AN OWL, low and fluty — the wood's near night call. One-shot, 2.3 s, scheduled
    /// sparsely by <c>EnvSound.TickNightCall</c>. ModBuild 222, and it is the user's own request:
    /// "eventuell hier und da noch ein ruf von tieren (was man so im Wald in der Nacht hört)".</summary>
    Owl,

    /// <summary>A SMALL BIRD FURTHER OFF — three thin whistles at ~2.8-3.1 kHz. One-shot, 0.78 s,
    /// the other half of the same request and deliberately at the opposite end of the band from
    /// <see cref="Owl"/>, so the wood does not repeat itself.</summary>
    NightBird,

    // ---- THE OTHER FIVE NIGHT CALLS — ModBuild 241 -------------------------------------------
    //
    // "Füge noch mehr verschiedene Tiersounds hinzu die zu einem Wald in der Nacht passen für mehr
    // Varianz (nicht mehr Häufigkeit)." Five more animals, and NOT ONE MORE EVENT PER MINUTE: the
    // schedule is untouched and a slot that was already going to sound now deals a card from a
    // seven-clip deck instead of tossing a coin between two. See THE WOOD'S VOCABULARY in this file
    // and THE NIGHT CALLS' DECK in EnvSound.cs.

    /// <summary>THE FEMALE TAWNY OWL'S "KE-WICK" — the sharp two-syllable contact call that pairs
    /// with <see cref="Owl"/>'s hoot, and the sound most people actually mean by "an owl at night".
    /// One-shot, 0.46 s, centroid 1926 Hz. See <c>MakeKeWick</c>.</summary>
    KeWick,

    /// <summary>A RED FOX BARKING — three hoarse, broadband barks. One-shot, 1.06 s, centroid
    /// 1390 Hz and the widest spectral spread in the bank (1.21 oct). The RAREST card in the deck
    /// with the roe deer. See <c>MakeFox</c>.</summary>
    Fox,

    /// <summary>A CORVID RASPING ON ITS ROOST — two dry "kraa"s. One-shot, 1.14 s, centroid
    /// 1150 Hz. See <c>MakeRaven</c>.</summary>
    Raven,

    /// <summary>A ROE DEER'S ALARM BARK — ONE short percussive cough, the shortest and most
    /// sudden thing the wood says. One-shot, 0.34 s, centroid 518 Hz, 5.3 ms attack. See
    /// <c>MakeRoeDeer</c>.</summary>
    RoeDeer,

    /// <summary>A YOUNG LONG-EARED OWL BEGGING — two long thin rasps, the "squeaky gate hinge" of a
    /// central-European wood in late summer. One-shot, 1.86 s, centroid 3532 Hz and NOISE where
    /// <see cref="NightBird"/> is a whistle. See <c>MakeOwletBeg</c>.</summary>
    OwletBeg,

    Creak,
    Breath,
    Drag,
    Fly,
    Fall,
    Settle,
}

// =================================================================================================
//  ENV SOUND — THE BANK. Every clip the environment can make, SYNTHESIZED into memory at spawn.
//  This file is the asset. There is no wav, no bundle entry, no download and no licence, and that
//  is a decision that was weighed rather than defaulted into — see WHY SYNTHESIS below.
// =================================================================================================

/// <summary>
/// The clip bank for <see cref="EnvSound"/>: a handful of mono <see cref="AudioClip"/>s built with
/// <see cref="AudioClip.Create(string,int,int,int,bool)"/> the first time an environment stands up,
/// kept for the session, and destroyed with the rest of the feature.
///
/// <para><b>WHY SYNTHESIS AND NOT FOUND RECORDINGS — the honest weighing, because the standing
/// instruction is "Suche nach coolen Assets im Internet statt es zwingend selber zu bauen" and this
/// file is a decision AGAINST that default.</b> Three things decided it, in order of weight:</para>
/// <list type="number">
/// <item><b>There is nowhere to put a recording.</b> A clip has to ship. The asset bundle is the
/// natural home (an <c>AudioClip</c> is data, not a MonoBehaviour, so unlike an
/// <c>AudioSource</c> it COULD live there) but the bundle is owned by other lanes this round and is
/// not available to this one. The remaining route is an <c>EmbeddedResource</c> in the plugin DLL,
/// which this project has never used for anything, would put binary blobs in git, and would need a
/// hand-written WAV parser because Unity cannot decode a byte[] into an <c>AudioClip</c> without
/// <c>UnityWebRequest</c> and a file on disk. That is a lot of new machinery for a texture that a
/// hundred lines of arithmetic produce exactly.</item>
/// <item><b>The licences are worse than they look.</b> Freesound's CC0 filter is genuinely
/// unencumbered (CC0 needs no attribution and permits redistribution, commercial included), so that
/// route is legally fine. The BBC Sound Effects library — the obvious first thought for a cellar
/// drip and a cave draught — is NOT: its 16 000 WAVs are BBC copyright released under the RemArc
/// licence, which permits personal, educational and research use only. Redistributing one inside a
/// publicly downloadable mod is exactly what it does not cover. Nothing here is CC0-blocked; it is
/// simply that the CC0 route buys nothing the arithmetic does not already give.</item>
/// <item><b>Synthesis is BETTER for this particular brief, not merely cheaper.</b> The requirement
/// is "dezent" and "nie aufdringlich überlagernd", and the specific failure mode of a recorded
/// ambience is the LOOP: the ear finds the seam and the one distinctive event inside the buffer
/// within a couple of passes, and from then on the environment is announcing itself. The beds here
/// are stationary filtered noise — statistically featureless, so there is no event to latch onto —
/// and every recognisable contour (the gust, the flame's breathing) is applied at RUNTIME by
/// <see cref="EnvSound"/> from LFOs whose periods are in irrational ratios, so the amplitude
/// envelope never repeats with the buffer. A recording cannot have that property; this can, and it
/// is the property the user actually asked for.</item>
/// </list>
///
/// <para><b>WHERE SYNTHESIS IS WEAKEST, stated rather than hidden.</b> The mouse squeak is the one
/// clip a recording would clearly beat: a real rodent call has formant structure that an FM chirp
/// only gestures at. It is kept because it is 90 ms long, plays at most once every 26 s, and sits
/// under a bed — at that exposure the difference is not worth the machinery above. If a later round
/// gets bundle access, <see cref="Squeak"/> is the ONE clip worth replacing with a CC0 recording.
/// The others (drip, flame, draught, rumble) are physically noise-and-resonance processes and the
/// arithmetic IS the honest model of them.</para>
///
/// <para><b>DETERMINISM.</b> Every clip is generated from <see cref="Rng"/>, an explicit xorshift
/// seeded per clip — never <c>UnityEngine.Random</c>, whose sequence is global state another
/// subsystem can disturb. Two clients therefore hold bit-identical buffers. Audio is local and
/// needs no wire, so this buys nothing on the network; it buys REPRODUCIBILITY, which is what makes
/// a listener's report ("the drip sounds wrong") mean the same thing on the machine that has to fix
/// it.</para>
///
/// <para><b>COST.</b> One shared 8 s wind bed, the candles' 7 s flutter, the fire's 6 s roar, and
/// twenty-one shorter clips (seventeen named plus the drip's, the crackle's and the ember's extra
/// realisations) at the output sample rate, mono. The DEVICE's
/// own figure was 21 clips / ~7.0 MB / 127 ms at 48 kHz for ModBuild 152 (Player.log:720); ModBuild
/// 153 adds the 7 s flutter for about +1.3 MB, so 22 clips and ~8.3 MB; ModBuild 154 adds the stone
/// (2.1 MB) and the wood's air (2.5 MB) for 24 clips and ~12.9 MB; ModBuild 222 lengthens the
/// wood's air from 13 s to 17 s (+0.8 MB) and adds the two NIGHT CALLS (2.3 s and 0.78 s, ~0.6 MB
/// together) for 26 clips and ~14.3 MB; and <b>ModBuild 223 GIVES 5.4 MB OF THAT BACK</b> by
/// deleting both room tones outright, for 24 clips and ~<b>8.9 MB</b>. They had been the biggest
/// single addition this bank ever made — a room tone plays for the WHOLE scenario, so it is the one
/// clip class where a short buffer is actually findable, and the two were the longest buffers here
/// for that reason. The user rejected the design rather than the length (see the block in
/// <see cref="EnvSoundClip"/>), so the memory goes with it. The mod runs on the PC in every
/// supported setup — the
/// headset is a display — so this is desktop RAM, not headset RAM. The <see cref="Build"/> log line
/// prints the real figure for the device's own rate rather than this estimate. Generated once per
/// session on the frame the room is first placed, and never touched again.
/// ModBuild 149 gives back the ice clip's 0.42 s and its ~0.70 ms of build time entirely (the
/// sound is DELETED, see the ruling block below) and spends about +0.35 ms of it again on the
/// bookshelf's rebuilt impact. Mono is not a saving but a REQUIREMENT: Unity refuses to spatialise
/// a stereo clip, and every clip here is meant to come from a place.</para>
/// </summary>
internal static class EnvSoundBank
{
    /// <summary>Sample rate of everything here. Taken from the live output device rather than
    /// assumed, so nothing is resampled on playback.</summary>
    private static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;

    /// <summary>
    /// THE WIND. Eight seconds of stationary broadband noise, looped by the TWO emitters that are
    /// air moving through an aperture — the window draught and the swamp canopy — and shaped per
    /// emitter by a runtime low-pass filter plus a runtime gain LFO.
    ///
    /// <para><b>NOTHING THAT IS NOT WIND MAY PLAY THIS BUFFER, and that sentence is the whole of two
    /// user reports.</b> The claim this doc used to make — one source of noise plus different filters
    /// is the correct physical model for several steady sounds — is true only while the sounds really
    /// are the same phenomenon at different scales, and it was stretched twice past that:</para>
    /// <list type="number">
    /// <item><b>THE SEATED FIRES came off it at ModBuild 152.</b> A fire PUFFS at a few hertz and
    /// CRACKLES, and neither is reachable by filtering stationary noise, so what the user was
    /// answered with when he asked for a fire sound was literally the draught, at a level that rose
    /// with the Fire element. See THE FIRE and <see cref="Roar"/>.</item>
    /// <item><b>THE CANDLE FLAMES came off it at ModBuild 153</b>, and the same round found the two
    /// faults hiding behind that: they were never Air-gated, so the wind buffer played in the cellar
    /// with Air fully off, and their gain carried a FIRE term, so infusing Fire made the wind clip
    /// louder. That is the ModBuild 152 report word for word. See THE CANDLE and
    /// <see cref="Flutter"/>.</item>
    /// </list>
    /// <para>What is left on this buffer is a window draught and a canopy full of leaves, which
    /// really are one phenomenon at two scales — and both of them are hard-zeroed by
    /// <c>EnvSound._windGate</c>, so this clip is INAUDIBLE whenever the Air element is down. That is
    /// now a property of the bank as well as of the caller: if a future emitter reaches for
    /// <c>EnvSoundClip.Bed</c> without going through <c>WindBed()</c>, <c>EnvSound.TickBeds</c> logs
    /// it as a defect rather than playing it.</para>
    ///
    /// <para>EIGHT SECONDS, and the length is chosen against the FILTER rather than against the
    /// ear: a 40 Hz-cornered low pass needs a good many cycles of its lowest passed frequency
    /// inside the buffer or the wrap becomes a click. 8 s at 40 Hz is 320 cycles. The wrap itself is
    /// cross-faded (see below) so even that cannot tick.</para>
    /// </summary>
    internal static AudioClip? Bed { get; private set; }

    /// <summary>THE CANDLE FLAME — narrow-band noise, fluttering at 11 Hz, with the wick's sputters
    /// in it. Looped, 7 s. See <see cref="MakeFlutter"/>, and see THE CANDLE below for the user
    /// report that took the candles off <see cref="Bed"/>.</summary>
    internal static AudioClip? Flutter { get; private set; }

    // NO `Stone` AND NO `NightAir` — the two room tones are deleted, clip and generator. See the
    // block in EnvSoundClip above for the ruling, and EnvSound.cs's THE ROOM TONES, DELETED for the
    // measurements. Their absence is the whole of ModBuild 223's cellar work.

    /// <summary>THE FIRE'S CONVECTIVE COLUMN — the low, breathy rush. Looped, 6 s. See
    /// <see cref="MakeRoar"/>, and see THE FIRE below for why the fire could not go on riding
    /// <see cref="Bed"/>.</summary>
    internal static AudioClip? Roar { get; private set; }

    /// <summary>ONE CRACKLE — variant 0 of four; <see cref="CrackleVariant"/> owns the draw.</summary>
    internal static AudioClip? Crackle { get; private set; }

    /// <summary>ONE EMBER SETTLING — variant 0 of two; <see cref="EmberVariant"/> owns the draw.</summary>
    internal static AudioClip? Ember { get; private set; }

    /// <summary>A single water drop landing in a shallow puddle. One-shot, ~0.28 s. This is
    /// VARIANT 0 of three — the "ordinary" drop; see <see cref="DripVariant"/> for why there are
    /// three and <see cref="MakeDrips"/> for what physically separates them.</summary>
    internal static AudioClip? Drip { get; private set; }

    /// <summary>The rat, heard: "Mäusepiepen" (user, verbatim). One-shot, ~0.09 s.</summary>
    internal static AudioClip? Squeak { get; private set; }

    /// <summary>The rat's feet on flagstones — a run of tiny dry ticks. One-shot, ~0.55 s.</summary>
    internal static AudioClip? Skitter { get; private set; }

    // THERE IS NO ICE CLIP, AND THE ABSENCE IS THE DECISION (user ruling, ModBuild 149, verbatim:
    // "Entferne das Geräusch für Eis komplett."). It was rebuilt once from the fracture physics up
    // one round earlier — seven power-law cracks, no beat, no pitch — and the answer after hearing
    // that rebuild was not "quieter" or "rarer" but that the sound should not exist. So there is no
    // `Frost` property, no `MakeFrost`, no `EnvSoundClip.Frost` and no scheduler for it; a gain of
    // zero or a clip nobody plays would leave the next reader believing there is a dial to find.
    // Ice still SHOWS on every surface — the frost crust is the environment shader's and is
    // untouched by this file. It simply makes no noise.

    /// <summary>Earth: a very low, slow settling. Looped, 6 s.</summary>
    internal static AudioClip? Rumble { get; private set; }

    /// <summary>AN OWL. One-shot, 2.3 s. See <see cref="MakeOwl"/> and THE NIGHT CALLS below.</summary>
    internal static AudioClip? Owl { get; private set; }

    /// <summary>A SMALL NIGHT BIRD, further off. One-shot, 0.78 s. See
    /// <see cref="MakeNightBird"/>.</summary>
    internal static AudioClip? NightBird { get; private set; }

    /// <summary>The FEMALE tawny owl's "ke-wick". One-shot, 0.46 s. See <see cref="MakeKeWick"/>.</summary>
    internal static AudioClip? KeWick { get; private set; }

    /// <summary>A red fox barking. One-shot, 1.06 s. See <see cref="MakeFox"/>.</summary>
    internal static AudioClip? Fox { get; private set; }

    /// <summary>A corvid rasping on its roost. One-shot, 1.14 s. See <see cref="MakeRaven"/>.</summary>
    internal static AudioClip? Raven { get; private set; }

    /// <summary>A roe deer's alarm bark. One-shot, 0.34 s. See <see cref="MakeRoeDeer"/>.</summary>
    internal static AudioClip? RoeDeer { get; private set; }

    /// <summary>A young long-eared owl begging. One-shot, 1.86 s. See
    /// <see cref="MakeOwletBeg"/>.</summary>
    internal static AudioClip? OwletBeg { get; private set; }

    // ---- the haunt cues ---------------------------------------------------------------------
    //
    // SIX CUES FOR SIX APPARITIONS, and the design rule they all obey is the coordinator's, which
    // is also the only rule that makes a horror cue work: THE FRIGHTENING SOUND IS NEVER THE LOUD
    // ONE. There is no stinger here, no hit, no impact transient with a fast attack — every one of
    // these starts under the bed and grows into it. A jump-scare would break "nie aufdringlich" and
    // the standing rule that the easter eggs may never disturb play, and it would also be worse
    // horror: the apparitions are built to be things you are not sure you saw, and a sound that
    // announces them converts a doubt into an event.

    /// <summary>Rope or old wood taking weight — a slow, irregular creak. ~1.3 s.</summary>
    internal static AudioClip? Creak { get; private set; }

    /// <summary>A breath that is not yours. Noise through a moving vocal-ish resonance. ~1.0 s.</summary>
    internal static AudioClip? Breath { get; private set; }

    /// <summary>Cloth, or a palm, dragging on stone. Band-limited noise with a slow sweep. ~1.4 s.</summary>
    internal static AudioClip? Drag { get; private set; }

    /// <summary>One fly, close, looping past. AM/FM buzz. ~1.6 s.
    ///
    /// <para><b>NO CARD DRAWS THIS TODAY, and it is kept deliberately rather than by neglect.</b>
    /// It was authored for the cellar's old card 2 — a face at floor level among the barrels.
    /// ModBuild 147 replaced that apparition with a stair-top door, which wanted a creak instead,
    /// and ModBuild 149 deleted the door as well: card 2 now draws nothing and sounds nothing (see
    /// <c>EnvSound.CueFor</c>). The clip costs 1.6 s of PCM and about 1.5 ms of the bank's ~112 ms
    /// to build, and the card catalogue has now been re-cut in THREE of the last four rounds;
    /// deleting a working generator that the next re-cut may well want back is a worse trade than
    /// the 1.5 ms. The standing condition for removing it is unchanged and has still not been met:
    /// if a round goes by with the catalogue STABLE and nothing claiming this clip, delete
    /// <see cref="MakeFly"/>, this property and <see cref="EnvSoundClip.Fly"/> together.</para></summary>
    internal static AudioClip? Fly { get; private set; }

    /// <summary>THE BOOKSHELF ARRIVING ON THE FLOOR, and the one clip in this bank the user has
    /// given written permission to be LOUD. The impact is at <c>t = 0</c> (ModBuild 148: the caller
    /// schedules it on the shelf's ACTUAL arrival, and a clip with its own run-up cannot be placed
    /// on an instant), and since ModBuild 149 it opens on a broadband CRACK with a scatter of
    /// contents behind it, because the previous version put 97% of its energy below 500 Hz and a
    /// headset speaker does not go there. ~0.9 s. See <see cref="MakeFall"/> for the measurements
    /// and <c>EnvSound.ShelfImpactGain</c> for the permission.</summary>
    internal static AudioClip? Fall { get; private set; }

    /// <summary>...and the same mass coming back up, slower and quieter, which is the more
    /// unsettling half. ~2.8 s, with its one contact at 1.162 s — the mirror of the fall's rebound,
    /// which is what the recovery curve actually does. See <see cref="MakeSettle"/>.</summary>
    internal static AudioClip? Settle { get; private set; }

    private static bool _built;

    /// <summary>True once <see cref="Build"/> has produced a usable bank.</summary>
    internal static bool Ready => _built && Bed != null;

    /// <summary>
    /// The three drops, indexed 0..2. <see cref="Drip"/> is element 0.
    ///
    /// <para>WHY AN ARRAY AND NOT THREE MORE <see cref="EnvSoundClip"/> MEMBERS. The enum exists so
    /// that <see cref="EnvSound"/>'s BED TABLE can be written as data ("this node gets Flutter") and so
    /// a log line can name the clip a cue chose. Nothing in that table would ever name
    /// <c>Drip2</c>/<c>Drip3</c>: the three are not three different sounds a designer picks between,
    /// they are one sound whose realisation is drawn per event. Putting them in the enum would
    /// advertise a choice that no caller has, and would leave <see cref="Bank"/> — the ONE
    /// translation, and the thing that keeps the table honest — with two cases nobody can reach.
    /// So the enum keeps one <c>Drip</c>, the accessor below owns the draw, and there is still
    /// exactly one place where "which drop" is decided.</para>
    /// </summary>
    private static readonly AudioClip?[] _drips = new AudioClip?[3];

    /// <summary>
    /// One of the three drops. <paramref name="which"/> is clamped rather than validated: the caller
    /// derives it from a hash, and a hash that returns exactly 1.0 (which
    /// <c>Haunt.Hash</c>'s <c>frac</c> is documented never to do, but which is one refactor away from
    /// being possible) must degrade to "the last drop" and not to an
    /// <see cref="System.IndexOutOfRangeException"/> inside the environment driver's per-frame path.
    /// Null before <see cref="Build"/>, and null forever on a device where the bank failed —
    /// <c>PlayShot</c> already treats a null clip as "that one cue is silent".
    /// </summary>
    internal static AudioClip? DripVariant(int which) =>
        _drips[which < 0 ? 0 : which >= _drips.Length ? _drips.Length - 1 : which];

    /// <summary>The four crackles and the two ember settles, for the same reason the three drops are
    /// an array rather than six more enum members (see <see cref="_drips"/>): nothing in
    /// <see cref="EnvSound"/>'s bed table would ever name <c>Crackle3</c> — they are one sound whose
    /// realisation is drawn per event, and the enum exists to let a TABLE name a clip.
    ///
    /// <para>FOUR AND TWO, and the ratio is the exposure. A crackle fires roughly every 2.2-4.0 s per
    /// fire site and an ember settles about one time in six, so over a minute of a Fire infusion in
    /// the cellar the player hears on the order of forty crackles and seven settles. Four
    /// realisations x a +-4% pitch jitter is enough that no two consecutive crackles are the same
    /// event; two is enough for something heard seven times.</para></summary>
    private static readonly AudioClip?[] _crackles = new AudioClip?[4];
    private static readonly AudioClip?[] _embers = new AudioClip?[2];

    /// <summary>One of the four crackles. Clamped rather than validated, exactly as
    /// <see cref="DripVariant"/> is and for the same reason.</summary>
    internal static AudioClip? CrackleVariant(int which) =>
        _crackles[which < 0 ? 0 : which >= _crackles.Length ? _crackles.Length - 1 : which];

    /// <summary>One of the two ember settles. Clamped, as above.</summary>
    internal static AudioClip? EmberVariant(int which) =>
        _embers[which < 0 ? 0 : which >= _embers.Length ? _embers.Length - 1 : which];

    /// <summary>
    /// The one translation from <see cref="EnvSoundClip"/> to a clip. Returns null rather than
    /// throwing for anything the bank failed to build, because every caller already has to handle a
    /// null clip (the bank is allowed to fail wholesale on a device with no audio) — so a missing
    /// clip degrades to "that one cue is silent" instead of to an exception inside the environment
    /// driver's per-frame path.
    /// </summary>
    internal static AudioClip? Bank(EnvSoundClip which) => which switch
    {
        EnvSoundClip.Bed => Bed,
        EnvSoundClip.Flutter => Flutter,
        EnvSoundClip.Owl => Owl,
        EnvSoundClip.NightBird => NightBird,
        EnvSoundClip.KeWick => KeWick,
        EnvSoundClip.Fox => Fox,
        EnvSoundClip.Raven => Raven,
        EnvSoundClip.RoeDeer => RoeDeer,
        EnvSoundClip.OwletBeg => OwletBeg,
        EnvSoundClip.Drip => Drip,
        EnvSoundClip.Squeak => Squeak,
        EnvSoundClip.Skitter => Skitter,
        EnvSoundClip.Rumble => Rumble,
        EnvSoundClip.Creak => Creak,
        EnvSoundClip.Breath => Breath,
        EnvSoundClip.Drag => Drag,
        EnvSoundClip.Fly => Fly,
        EnvSoundClip.Fall => Fall,
        EnvSoundClip.Settle => Settle,
        EnvSoundClip.Roar => Roar,
        EnvSoundClip.Crackle => Crackle,
        EnvSoundClip.Ember => Ember,
        _ => null,
    };

    /// <summary>
    /// Build every clip. Idempotent, and safe to call from the environment's per-frame path: the
    /// guard is the first line, so the cost after the first call is one bool read.
    ///
    /// <para>Never throws. A device with no audio output, a sample rate of 0, an
    /// <see cref="AudioClip.Create(string,int,int,int,bool)"/> that refuses — all of them must
    /// degrade to "the environment is silent", never take the environment driver down with them.
    /// <see cref="Ready"/> is what the caller tests, and it stays false.</para>
    /// </summary>
    internal static void Build()
    {
        if (_built)
            return;
        _built = true;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            int rate = Rate;

            Bed = MakeBed(rate);
            Flutter = MakeFlutter(rate);
            Roar = MakeRoar(rate);
            Crackle = MakeCrackles(rate);   // fills _crackles and hands back element 0
            Ember = MakeEmbers(rate);       // fills _embers  and hands back element 0
            Drip = MakeDrips(rate);   // fills _drips and hands back element 0
            Squeak = MakeSqueak(rate);
            Skitter = MakeSkitter(rate);
            Rumble = MakeRumble(rate);
            Owl = MakeOwl(rate);
            NightBird = MakeNightBird(rate);
            KeWick = MakeKeWick(rate);
            Fox = MakeFox(rate);
            Raven = MakeRaven(rate);
            RoeDeer = MakeRoeDeer(rate);
            OwletBeg = MakeOwletBeg(rate);

            Creak = MakeCreak(rate);
            Breath = MakeBreath(rate);
            Drag = MakeDrag(rate);
            Fly = MakeFly(rate);
            Fall = MakeFall(rate);
            Settle = MakeSettle(rate);

            // ONE LINE, ONCE PER SESSION, AND IT EARNED ITS PLACE. This synthesis runs on the main
            // thread on the frame the room is first placed, and until ModBuild 146 it wrote nothing
            // at all on the way through — so when MakeCreak spun forever there (see
            // EnvSoundSchedule), Player.log ended on the environment's "ROOM placed" and the next
            // suspect was every one of the dozen things that also start on that frame. A bank that
            // says it finished, and how long it took, turns that whole class of report into one
            // glance: the line is there and the freeze is elsewhere, or the line is missing and it
            // is here.
            VRLog.Info("Core", $"ENV SOUND bank synthesized in {watch.Elapsed.TotalMilliseconds:F0} ms — " +
                               $"{_made.Count} clip(s) at {rate} Hz, ~{TotalBytes() / 1024f / 1024f:F1} MB, " +
                               "built once per session on the frame the room is first placed and reused by " +
                               "every emitter and every cue from then on.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Core", $"ENV SOUND bank could not be synthesized ({ex.GetType().Name}: " +
                               $"{ex.Message}) — the environment stays silent this session. Nothing " +
                               "else is affected: every emitter tests EnvSoundBank.Ready before it " +
                               "is created, so a failed bank is a missing feature, not a fault.");
        }
    }

    /// <summary>
    /// Drop every clip. Called from <see cref="EnvSound.StandDown"/>'s teardown route only — NOT
    /// from an ordinary stand-down, because the bank is style-independent and rebuilding ~2 MB of
    /// noise on every mixed-reality toggle would be pure waste. What must never survive is a
    /// SOURCE (see <see cref="EnvSound"/>); a clip nobody plays is inert.
    /// </summary>
    internal static void Release()
    {
        if (!_built)
            return;
        _built = false;

        // Every clip made by Finish is in _made, Bed included — so the ONE loop below destroys
        // everything exactly once. A separate "kill the bed first" step used to live here and was
        // a double-destroy waiting to happen.
        Bed = null; Flutter = null;
        Drip = null; Squeak = null; Skitter = null;
        Rumble = null; Owl = null; NightBird = null;
        KeWick = null; Fox = null; Raven = null; RoeDeer = null; OwletBeg = null;
        Creak = null; Breath = null; Drag = null; Fly = null; Fall = null; Settle = null;
        Roar = null; Crackle = null; Ember = null;

        // The three drops are IN _made as well (Finish put them there), so this drops the
        // references only — the destroy loop below is still the single place anything is destroyed.
        // Clearing it here and not there is what keeps that true.
        System.Array.Clear(_drips, 0, _drips.Length);
        System.Array.Clear(_crackles, 0, _crackles.Length);
        System.Array.Clear(_embers, 0, _embers.Length);

        foreach (AudioClip? c in _made)
        {
            if (c != null)
                Object.Destroy(c);
        }
        _made.Clear();
        _shape.Clear();
    }

    private static readonly System.Collections.Generic.List<AudioClip?> _made = new();

    /// <summary>
    /// THE MEASURED SHAPE of every clip <see cref="Finish"/> wrapped, index-parallel to
    /// <see cref="_made"/>: the largest absolute sample in the buffer and the instant it occurs.
    ///
    /// <para><b>WHY THE BANK MEASURES ITSELF.</b> Nobody working on this can hear it. Every level
    /// argument in this file and in <see cref="EnvSound"/> is written against numbers produced by
    /// running the generators OUTSIDE Unity, which is honest but is not the same buffer the device
    /// actually plays — the sample rate differs, and a generator edit can silently invalidate every
    /// figure in a doc comment without anything failing. Recording the peak here costs one pass over
    /// a buffer that has just been written anyway, and it lets a cue's log line state what the
    /// player's headset was really handed: gain, master and PEAK, on one line, from the device.
    /// That is the whole verification path for the one sound in this feature the user is allowed to
    /// hear loudly (see <c>EnvSound.ShelfImpactGain</c>).</para>
    /// </summary>
    private struct ClipShape
    {
        internal float Peak;
        internal float PeakSeconds;
    }

    private static readonly System.Collections.Generic.List<ClipShape> _shape = new();

    /// <summary>The measured peak sample of a built clip, 0..1, and the instant it occurs — the
    /// clip's ATTACK, which is the number that says whether an impact peaks on its contact or
    /// somewhere inside a run-up. Returns zeros for null and for anything this bank did not build,
    /// because a caller that cannot find the clip must log "unknown" rather than a fiction.</summary>
    internal static void MeasuredShape(AudioClip? clip, out float peak, out float peakSeconds)
    {
        peak = 0f;
        peakSeconds = 0f;
        if (clip == null)
            return;
        // A linear scan over at most fifteen entries, on a path that runs at most once per haunt
        // event. A dictionary here would be a lookup table to keep in step for no measurable gain.
        for (int i = 0; i < _made.Count && i < _shape.Count; i++)
        {
            if (!ReferenceEquals(_made[i], clip))
                continue;
            peak = _shape[i].Peak;
            peakSeconds = _shape[i].PeakSeconds;
            return;
        }
    }

    /// <summary>Bytes of PCM the bank holds, for the one build log line. Mono 32-bit float, which is
    /// what <see cref="Finish"/> creates.</summary>
    private static long TotalBytes()
    {
        long total = 0;
        foreach (AudioClip? c in _made)
        {
            if (c != null)
                total += (long)c.samples * c.channels * 4;
        }
        return total;
    }

    // =============================================================================================
    //  THE GENERATORS
    // =============================================================================================

    /// <summary>
    /// Wrap a finished float buffer into a clip and remember it for <see cref="Release"/>.
    /// Mono and at the output rate — the two properties every caller depends on.
    /// </summary>
    private static AudioClip Finish(string name, float[] data, int rate)
    {
        // MEASURE BEFORE WRAPPING — see _shape. One pass over a buffer that is already hot in cache.
        float peak = 0f;
        int peakAt = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float a = data[i] < 0f ? -data[i] : data[i];
            if (a <= peak)
                continue;
            peak = a;
            peakAt = i;
        }

        var clip = AudioClip.Create("GhvrEnvSound." + name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        _made.Add(clip);
        // The two lists are index-parallel BY CONSTRUCTION: this is the only place either grows, and
        // Release is the only place either shrinks, and it clears both.
        _shape.Add(new ClipShape { Peak = peak, PeakSeconds = peakAt / (float)Mathf.Max(rate, 1) });
        return clip;
    }

    /// <summary>
    /// Cross-fade the last <paramref name="tail"/> samples of a LOOPING buffer over its own head,
    /// so the wrap has no discontinuity and cannot tick. Applied to every looped clip; one-shots
    /// do not need it because they end in silence.
    ///
    /// <para>The fade is equal-POWER (sin/cos), not linear: two independent noise signals summed
    /// with linear weights lose 3 dB in the middle of the fade, which is audible on a bed as a
    /// periodic dip — the exact "the ear latches onto the loop" failure this whole file is built to
    /// avoid.</para>
    /// </summary>
    private static void LoopFade(float[] d, int tail)
    {
        int n = d.Length;
        if (tail <= 0 || tail * 2 >= n)
            return;
        for (int i = 0; i < tail; i++)
        {
            float t = (i + 0.5f) / tail;
            float a = Mathf.Sin(t * Mathf.PI * 0.5f);   // incoming head
            float b = Mathf.Cos(t * Mathf.PI * 0.5f);   // outgoing tail
            d[i] = d[i] * a + d[n - tail + i] * b;
        }
        // The tail has been folded into the head; blank it so the buffer ends where the head began.
        for (int i = n - tail; i < n; i++)
            d[i] = d[i - (n - tail)];
    }

    /// <summary>Normalise to a target peak. Every generator ends with this so the LEVELS in
    /// <see cref="EnvSound"/> are the only place loudness is decided — a generator that quietly
    /// ran hot would otherwise defeat the gain budget from underneath it.</summary>
    private static void Normalise(float[] d, float peak)
    {
        float max = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float a = d[i] < 0f ? -d[i] : d[i];
            if (a > max)
                max = a;
        }
        if (max <= 1e-6f)
            return;
        float g = peak / max;
        for (int i = 0; i < d.Length; i++)
            d[i] *= g;
    }

    /// <summary>One-pole low pass, in place. <paramref name="hz"/> is the -3 dB corner.</summary>
    private static void LowPass(float[] d, int rate, float hz)
    {
        float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * hz / rate));
        float y = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            y += a * (d[i] - y);
            d[i] = y;
        }
    }

    /// <summary>One-pole high pass, in place (the low-passed part subtracted out).</summary>
    private static void HighPass(float[] d, int rate, float hz)
    {
        float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * hz / rate));
        float y = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            y += a * (d[i] - y);
            d[i] -= y;
        }
    }

    /// <summary>
    /// SATURATE a buffer through <c>tanh</c>, in place, normalised so that a full-scale input still
    /// comes out full scale. <paramref name="drive"/> is how hard: 0 leaves the buffer alone, 1 is
    /// barely there, 3 compresses the top of the range about three to one.
    ///
    /// <para><b>WHY A GENERATOR IN THIS FILE IS ALLOWED A MASTERING STEP AT ALL</b> — the only one,
    /// and only on the one clip the user has granted an exception for. Every level in this feature is
    /// decided by a PEAK: the generators end in <see cref="Normalise"/> and
    /// <c>EnvSound.PlayShot</c> clamps a gain. What the ear judges an impact by is not its peak but
    /// its energy over roughly the twenty milliseconds it integrates over, and the two came apart
    /// badly here. The measured crest factor of the ModBuild 149 arrival is <b>15 dB</b> — the peak
    /// is ONE SAMPLE of the contact's noise burst, so fifteen of the exception's decibels were being
    /// spent on a sample nobody can hear, and every other part of the bang was fifteen decibels down
    /// from the level the budget said it was at.</para>
    ///
    /// <para><b>AND ON THIS HARDWARE THE DISTORTION IS THE POINT, not a side effect.</b> The clip's
    /// weight lives in a 78 Hz carcass mode; a Quest 3 speaker reproduces essentially nothing below
    /// about 150-250 Hz, so that mode arrives as silence. Saturation puts its harmonics at 156, 234,
    /// 312 Hz — inside the band the speaker DOES have — and the ear fuses a harmonic series back into
    /// its missing fundamental. This is the same mechanism every small-speaker "bass enhancement"
    /// uses, obtained here for free out of a step that was worth doing anyway.</para>
    ///
    /// <para>MEASURED, on the finished buffer: the loudest 20 ms window, after a 200 Hz high pass
    /// standing in for the headset's own low-end rolloff, goes from 0.157 to 0.369 — <b>+7.4 dB at
    /// exactly the same peak</b>. See <see cref="MakeFall"/>'s table.</para>
    /// </summary>
    private static void SoftClip(float[] d, float drive)
    {
        if (!(drive > 0f))
            return;
        // Normalise to unity FIRST, so `drive` means the same thing whatever the buffer arrived at —
        // a saturator whose amount depends on the caller's incoming level is a saturator nobody can
        // reason about.
        Normalise(d, 1f);
        // System.Math.Tanh and not Mathf: net472 has no MathF, and this runs once per session on a
        // buffer of 43k samples inside a bank build that already takes ~100 ms.
        float k = (float)System.Math.Tanh(drive);
        for (int i = 0; i < d.Length; i++)
            d[i] = (float)System.Math.Tanh(drive * d[i]) / k;
    }

    // ---- the bed --------------------------------------------------------------------------------

    private static AudioClip MakeBed(int rate)
    {
        int n = rate * 8;
        var d = new float[n];
        var r = new Rng(0x5EEDBEDu);

        // Pink-ish rather than white: Voss-McCartney's cheap cousin, three one-pole low passes at
        // octave-spaced corners summed. White noise is unpleasant to sit under for minutes at a
        // time (all the energy is at the top, where the ear is most sensitive and where the game's
        // UI cues live); a 1/f-ish slope is what natural airflow actually sounds like and it leaves
        // the top of the band free for the game.
        float a1 = 0f, a2 = 0f, a3 = 0f;
        float k1 = 1f - Mathf.Exp(-2f * Mathf.PI * 40f / rate);
        float k2 = 1f - Mathf.Exp(-2f * Mathf.PI * 320f / rate);
        float k3 = 1f - Mathf.Exp(-2f * Mathf.PI * 2600f / rate);
        for (int i = 0; i < n; i++)
        {
            float w = r.Next();
            a1 += k1 * (w - a1);
            a2 += k2 * (w - a2);
            a3 += k3 * (w - a3);
            d[i] = a1 * 0.62f + a2 * 0.30f + a3 * 0.14f;
        }

        LoopFade(d, rate / 2);
        Normalise(d, 0.85f);
        return Finish("Bed", d, rate);
    }

    // =============================================================================================
    //  THE FIRE — ModBuild 152, and it is the first fire sound this mod has ever made.
    // =============================================================================================
    //
    //  USER REQUEST, verbatim: "Geb auch Feuer dezente Geräusche."
    //
    //  WHAT WAS THERE BEFORE, stated plainly because it is the whole reason this block exists: THE
    //  FIRE'S SOUND WAS A DRAUGHT. The cellar's three candle groups have ridden `Bed` since the
    //  feature shipped — the SAME buffer as the window draught and the swamp canopy — under a gain
    //  LFO with a term in ElementMood.Live(0). So "the fire answers a Fire infusion" was true of the
    //  LEVEL and of nothing else: what got louder was wind. The six real fires the content lane
    //  seated in the cellar (two on the crates, two on the casks, two in the bookcase) and the five
    //  in the wood (the snag, three along the deadfall, the brushwood) made no sound whatsoever.
    //
    //  A FIRE IS THREE SOUNDS AND THEY ARE WORTH SEPARATING, because the MIX is what makes it read
    //  as fire rather than as noise:
    //
    //    1. THE ROAR — the convective column. Air is being dragged in at the base, heated, and thrown
    //       up; that is turbulence, so it is broadband noise, and it is LOW because the eddies that
    //       carry the energy are the size of the fire. It is also not steady: a flame puffs at a few
    //       hertz (the same Kelvin-Helmholtz instability the art lane measured at 3-8 Hz for the
    //       VISIBLE flicker — see FireHz in the bake), and that puffing is most of what separates a
    //       fire from a vent. See MakeRoar.
    //    2. THE CRACKLE — and this is the layer that IDENTIFIES it. Wood cells are closed vessels
    //       full of water and volatiles; heated past ~200 C they burst, each one throwing a tiny
    //       pressure step into the air. That is a broadband transient a few milliseconds long, and
    //       they arrive at an irregular rate because the cells are independent. Nothing else in a
    //       room sounds like this, and a fire without it is a heater. See MakeCrackles.
    //    3. THE SETTLE — a lump of charcoal losing its footing in the bed and shifting. Rarer than
    //       the crackle, longer, and much duller: charcoal is porous and dead, so it thuds. See
    //       MakeEmbers.
    //
    //  WHY IT CANNOT RIDE `Bed` (the cheap answer, weighed and rejected). One buffer plus two filters
    //  is the correct physical model for a fire and a draught INSOFAR AS BOTH ARE STEADY NOISE — and
    //  a fire is not steady. The puffing is an amplitude envelope at a few hertz, and the only
    //  runtime shaping this feature has is EnvSound's per-frame gain walk, which MoveTowards at
    //  0.6 units per second: it physically cannot follow a 5 Hz envelope, and raising that rate would
    //  make every OTHER bed click on an element ramp. The envelope therefore has to be inside the
    //  buffer, and once it is, the buffer is a different buffer. The crackle settles it beyond
    //  argument: no filter turns stationary noise into transients.
    //
    //  THE HARDWARE DECIDED THE BANDS, and this is the ModBuild 150 lesson applied before the fact
    //  rather than after it. A Quest 3 speaker returns essentially nothing below about 200 Hz. The
    //  bookshelf's arrival spent 72.7% of its energy under 200 Hz, the user heard nothing, and the
    //  fix was to move that energy up. So:
    //    * the roar is high-passed at 230 Hz (twice, 12 dB/oct) rather than being the 40 Hz-cornered
    //      pink noise `Bed` is — 11.4% of it lands under 200 Hz instead of the majority;
    //    * the crackle is put SQUARELY in 1-5 kHz, where the speaker and the ear are both at their
    //      best: 71-87% of every variant's energy is in that band, against the 7.6% the shipped
    //      bookshelf clip had;
    //    * and the roar's CEILING is 820 Hz (three poles), which keeps the one CONTINUOUS layer out
    //      of the 1-4 kHz speech band the class doc protects. The crackle is allowed in that band by
    //      the class doc's own duration escape clause and by nothing else: it is 55 ms long with a
    //      1.0-1.2 ms decay to -20 dB, i.e. shorter than the drip's transient, and the clause is
    //      about duration rather than about taste.
    //
    //  MEASURED, off the finished buffers, by a replica of these generators run outside Unity
    //  (.planning/envsound-replica/fire.py). The replica is validated rather than asserted: it
    //  reproduces the SHIPPED Fall clip's published table exactly (peak 0.980 at 2.44 ms, RMS 0.0697,
    //  90% of peak in 0.19 ms, -20 dB in 27 ms, centroid 1530 Hz, bands 26.9/5.3/34.5/17.3/10.0/5.9),
    //  so the figures below are the same arithmetic on the same harness. The DEVICE's own peak and
    //  attack for any of these is printed by MeasuredShape wherever a cue logs.
    //
    //  THE ROAR'S RMS AND ITS 20 ms WINDOW MOVED AT ModBuild 153 (0.0628 -> 0.0567, 0.1015 ->
    //  0.1019) and nothing else in this table did. That is the puff envelope becoming real; see
    //  RoarPuffSigmas for the defect and the measurement. The SPECTRUM is untouched, because
    //  multiplying by a slow broadband envelope does not move a band share.
    //
    //                 len     peak  peak at   90%     RMS   -20dB   20 ms   centroid
    //    Roar        6.000 s  0.300    43 ms  43 ms  0.0567      -  0.1019     446 Hz
    //    Crackle0    0.055 s  0.950  0.48 ms 0.48ms  0.0710  1.2 ms 0.1086    3175 Hz
    //    Crackle1    0.055 s  0.780  0.54 ms 0.52ms  0.0457  1.2 ms 0.0656    2076 Hz
    //    Crackle2    0.055 s  0.950  0.75 ms 0.73ms  0.0680  1.0 ms 0.1127    1856 Hz
    //    Crackle3    0.055 s  0.860  0.56 ms 0.56ms  0.0647  1.2 ms 0.1042    2634 Hz
    //    Ember0      0.220 s  0.550  2.02 ms 1.67ms  0.0365 11.1 ms 0.1111    1646 Hz
    //    Ember1      0.220 s  0.550  1.69 ms 1.69ms  0.0406 10.3 ms 0.1176    1633 Hz
    //
    //    energy by band     0-200  200-500  500-1k    1-2k    2-5k   5-24k    1-5 kHz
    //    Roar               11.4%    56.2%   28.8%    3.5%    0.1%    0.0%       3.6%
    //    Crackle0            0.0%     0.9%    9.8%   28.2%   43.2%   17.9%      71.4%
    //    Crackle1            0.0%     1.1%   17.6%   37.0%   39.8%    4.5%      76.8%
    //    Crackle2            0.0%     0.5%   10.2%   60.9%   25.6%    2.7%      86.5%
    //    Crackle3            0.0%     1.3%   13.6%   16.3%   62.6%    6.2%      78.9%
    //    Ember0              0.2%     3.1%   57.7%   12.0%   20.9%    5.9%      33.0%
    //    Ember1              0.1%     5.4%   49.0%   23.1%   17.2%    5.2%      40.3%
    //
    //  ("20 ms" is the loudest 20 ms RMS window through a one-pole 200 Hz high pass — the crude
    //  stand-in for the headset's own low-end rolloff that the bookshelf round introduced, and the
    //  number that best predicts what the player actually hears. The roar has no "-20 dB" because it
    //  is a loop and never decays.)
    //
    //  THE THREE LAYERS ARE THREE DIFFERENT THINGS IN THE MIX and that is deliberate: the roar is a
    //  BED (it plays continuously while the fire is lit and sits under everything at a level the
    //  distance already softens), the crackle is an EVENT (statistically timed, see
    //  EnvSound.TickFire), and the settle is a rarer event drawn from the same schedule. The level
    //  each ends up at is EnvSound's, not this file's — every generator here ends in Normalise for
    //  the same reason all the others do.
    //
    //  REJECTED:
    //    * A SINGLE "FIRE" LOOP WITH CRACKLES BAKED IN. This is what a recording would be, and it is
    //      the loop-finding failure the class doc is built to avoid, in its worst form: the crackles
    //      ARE the recognisable events, so a buffer containing them announces its own period within
    //      two or three passes. Statistical scheduling is not a flourish here, it is the only way a
    //      crackle can be heard for a minute without becoming a rhythm.
    //    * ONE ROAR CLIP PER SITE, detuned. Six buffers to make three fires differ, when the three
    //      already differ by their PLACE, their distance and their independent crackle streams. The
    //      one clip is started at a different offset per bed by AddBed's decorrelation, which is the
    //      same argument the three candle flames already rest on.
    //    * PUTTING THE CRACKLE ON THE SHARED ONE-SHOT POOL. Three sites at a 2.2 s mean is roughly
    //      1.4 crackles a second across a room, through a pool of three voices that the drip, the
    //      rat and every haunt cue also use — the fire would have taken the pool. Each site's crackle
    //      goes through its OWN bed source with PlayOneShot instead; see EnvSound.TickFire, where
    //      that also turns out to be what makes the crackle inherit the fire's gate, its rolloff and
    //      its position for free.

    /// <summary>Length of the roar loop, seconds. Six is a compromise the ear and the budget both
    /// sign: long enough that a 230 Hz-cornered high pass has 1380 cycles of its lowest passed
    /// frequency inside the buffer (so the equal-power wrap cannot tick), and 1.15 MB at 48 kHz
    /// rather than the 1.54 the shared bed's eight seconds cost. The thing that would make a loop
    /// findable — a recognisable EVENT inside it — is not in this buffer at all: the crackles are
    /// scheduled at runtime.</summary>
    private const float RoarSeconds = 6f;

    /// <summary>The roar's pink-noise corners and their weights, as <see cref="MakeBed"/>'s are.
    /// Pushed UP against the bed's 40/320/2600: the bed is a draught, whose energy really does run
    /// down to nothing, while a fire's turbulent eddies are the size of the FIRE — a 0.6 m column
    /// radiates around a few hundred hertz and has very little to say below that. These corners put
    /// the slope's knee inside the band the high pass then keeps, so the filter is trimming a tail
    /// rather than removing the signal.</summary>
    private static readonly float[] RoarPinkHz = { 90f, 420f, 1500f };
    private static readonly float[] RoarPinkMix = { 0.46f, 0.34f, 0.20f };

    /// <summary>
    /// THE PUFFING, and it is what makes this a fire rather than a vent. A flame's plume is
    /// unstable at a few hertz — the art lane measured the VISIBLE flicker at 3-8 Hz and condemned
    /// its own 0.63-1.03 Hz sway on exactly that ground (see <c>FireHz</c> in the bake) — and the
    /// sound puffs with the picture because they are the same instability.
    ///
    /// <para><b>IT IS NOISE-DERIVED, NOT A SINE, AND THAT IS THE POINT.</b> An envelope built from
    /// LFOs has a PERIOD, and a period inside a looping buffer is the one thing item 6 of the class
    /// doc's "never intrusive" list forbids — the ear would find 6 s within a few passes. A second
    /// independent noise stream, twice low-passed at <see cref="RoarPuffHz"/>, has a
    /// characteristic RATE and no period whatsoever, which is also the honest model: turbulence is
    /// not periodic. Twice rather than once because one pole leaves a hiss on the envelope that
    /// amplitude-modulates the carrier into a second noise floor.</para></summary>
    private const float RoarPuffHz = 5.5f;

    /// <summary>How far the puffing may pull the roar down — the envelope runs
    /// <see cref="RoarPuffFloor"/>..1. A fire never goes silent between puffs (the column is
    /// continuous; what changes is how hard it is being driven), so a floor of 0.42 is about 7.5 dB
    /// of breathing, which is a fire seen to surge rather than a tremolo.</summary>
    private const float RoarPuffFloor = 0.42f;

    /// <summary>
    /// HOW MANY STANDARD DEVIATIONS OF THE PUFF STREAM SPAN THE FULL FLOOR-TO-ONE RANGE — and this
    /// constant exists because ModBuild 152's roar did not actually puff.
    ///
    /// <para><b>THE DEFECT, MEASURED.</b> The shipped generator normalised the envelope stream by its
    /// own PEAK (<c>e2 / emax</c>). A twice-low-passed noise stream's peak is a rare excursion: the
    /// replica measures peak/sigma = <b>3.13</b> over the 6 s buffer, so dividing by the peak
    /// squeezes the typical excursion into the middle of the range. <see cref="RoarPuffFloor"/>
    /// claims 20·log10(1/0.42) = <b>7.54 dB</b> of breathing; what the buffer actually carried was a
    /// 5-95% swing of <b>3.81 dB</b>. The stationary draught bed's OWN envelope — the accidental
    /// fluctuation of band-limited noise through a 20 ms window, with no envelope applied at all —
    /// measures <b>4.02 dB</b>. So the fire's deliberate puff was SMALLER than the wind's accident,
    /// and "a fire puffs and a draught does not" (this block's own argument for why the fire could
    /// not ride <c>Bed</c>) was true of the intent and false of the buffer.</para>
    ///
    /// <para><b>THAT IS THE MEASURABLE HALF OF THE ModBuild 152 USER REPORT.</b> "Beim Feuer Geräusch
    /// ist auch immer das Wind geräusch mit dabei" had an obvious cause — the candle beds were
    /// literally playing the draught, see THE CANDLE — and this second one behind it: with the puff
    /// flattened, the roar was steady low-passed broadband noise, which IS a wind. The band alone
    /// could not separate them either; the replica measures the roar's audible centroid at 436 Hz
    /// against the draught's 598 Hz, well inside what two noises can share.</para>
    ///
    /// <para><b>1.8 SIGMA, and the clamp is the point rather than a safety net.</b> Normalising by
    /// k·sigma and clamping to [0,1] makes the realised swing a stated number instead of a property
    /// of one buffer's luckiest sample. At k = 1.8 the measured 5-95% swing is about <b>6.8 dB</b> on
    /// the envelope stream and <b>7.47 dB</b> on the finished buffer, against the draught's 4.02 dB,
    /// and about 6.6% of samples sit on a clamp — i.e. the surge tops out and the lull bottoms out, which is
    /// what a fire being fed does. A larger k gives the shipped defect back gradually (k = 2.5 is
    /// 4.81 dB); a smaller one is a square wave.</para>
    ///
    /// <para>THE COST, stated: the deeper envelope lowers the buffer's RMS at the same peak, so the
    /// roar is <b>0.9 dB</b> quieter overall (0.0567 against 0.0628). That is the right direction for
    /// this feature and it is not compensated.</para></summary>
    private const float RoarPuffSigmas = 1.8f;

    /// <summary>The roar's band. THE FLOOR IS THE HARDWARE (a Quest 3 speaker gives essentially
    /// nothing under ~200 Hz, so energy below it is energy spent on silence — the bookshelf's 72.7%
    /// is the measured cost of not knowing that), applied TWICE for 12 dB/oct because one pole leaves
    /// a third of the buffer's energy under 200 Hz instead of the 11.4% two do.
    ///
    /// <para>THE CEILING IS THE CLASS DOC. This is a CONTINUOUS layer, so it is held out of the
    /// 1-4 kHz band where speech intelligibility and the game's UI cues live — three poles at 820 Hz
    /// leave 3.6% of the roar in 1-5 kHz and 0.1% above 2 kHz. The crackle goes into that band
    /// instead, and it is allowed there by the duration clause and not by this one.</para></summary>
    private const float RoarLoHz = 230f;
    private const float RoarHiHz = 820f;
    private const int RoarLoPoles = 2;
    private const int RoarHiPoles = 3;

    /// <summary>The roar's peak. LOW, and it is the number that makes the crackle audible: both
    /// layers come out of ONE AudioSource whose volume is set for the CRACKLE (see
    /// <c>EnvSound.FireBedGain</c>), so the roar's own normalisation is where the balance between the
    /// two is actually decided. 0.30 against the crackle's 0.95 is a 10 dB spread in peak and about
    /// 4.5 dB in the 20 ms window the ear integrates — the roar is the floor the crackles stand
    /// on.</summary>
    private const float RoarPeak = 0.30f;

    /// <summary>
    /// THE CONVECTIVE COLUMN. Pink-ish noise inside the fire's own band, multiplied by a turbulent
    /// envelope, looped. See THE FIRE above for the physics, the bands and the measurements.
    ///
    /// <para>TWO PASSES OVER ONE BUFFER, and the reason is memory rather than style: the envelope has
    /// to be NORMALISED (a twice-low-passed noise stream's peak is not predictable in closed form, so
    /// a typed scale would make the puff depth depend on the sample rate), which means knowing its
    /// maximum before it can be applied. Writing it into <c>d</c>, measuring, and then overwriting
    /// <c>d</c> with the carrier times the envelope costs one extra pass and saves a second 1.15 MB
    /// buffer inside a bank build that already runs on the frame the room is placed. The two Rng
    /// streams are independent instances, so splitting the interleaved loop in two does not move a
    /// single draw.</para>
    /// </summary>
    private static AudioClip MakeRoar(int rate)
    {
        int n = (int)(rate * RoarSeconds);
        var d = new float[n];

        // ---- pass 1: THE ENVELOPE, into d, and its SIGMA. Not its peak — see RoarPuffSigmas for
        // what normalising by the peak cost, measured.
        var er = new Rng(0xF12E0000u ^ 0x5A5A5A5Au);
        float ke = 1f - Mathf.Exp(-2f * Mathf.PI * RoarPuffHz / rate);
        float e1 = 0f, e2 = 0f;
        // The sum of squares is accumulated in DOUBLE: 288 000 terms of ~6e-5 each summed in float
        // loses the tail of the accumulator to rounding, and this is a number the puff depth is
        // divided by. The mean is zero by construction (the stream is a filtered zero-mean noise),
        // so the RMS is the standard deviation and no second pass is needed.
        double acc = 0.0;
        for (int i = 0; i < n; i++)
        {
            float v = er.Next();
            e1 += ke * (v - e1);
            e2 += ke * (e1 - e2);
            d[i] = e2;
            acc += (double)e2 * e2;
        }
        float span = RoarPuffSigmas * (float)System.Math.Sqrt(acc / Mathf.Max(n, 1));
        if (!(span > 1e-9f))
            span = 1f;

        // ---- pass 2: THE CARRIER, times that envelope mapped onto [RoarPuffFloor, 1].
        var r = new Rng(0xF12E0000u);
        float k1 = 1f - Mathf.Exp(-2f * Mathf.PI * RoarPinkHz[0] / rate);
        float k2 = 1f - Mathf.Exp(-2f * Mathf.PI * RoarPinkHz[1] / rate);
        float k3 = 1f - Mathf.Exp(-2f * Mathf.PI * RoarPinkHz[2] / rate);
        float a1 = 0f, a2 = 0f, a3 = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = r.Next();
            a1 += k1 * (w - a1);
            a2 += k2 * (w - a2);
            a3 += k3 * (w - a3);
            float pink = a1 * RoarPinkMix[0] + a2 * RoarPinkMix[1] + a3 * RoarPinkMix[2];
            float u = Mathf.Clamp01(0.5f * (1f + d[i] / span));
            d[i] = pink * (RoarPuffFloor + (1f - RoarPuffFloor) * u);
        }

        for (int p = 0; p < RoarLoPoles; p++)
            HighPass(d, rate, RoarLoHz);
        for (int p = 0; p < RoarHiPoles; p++)
            LowPass(d, rate, RoarHiHz);

        LoopFade(d, rate / 2);
        Normalise(d, RoarPeak);
        return Finish("Roar", d, rate);
    }

    /// <summary>Length of one crackle, and the window its pops land in. A crackle is not ONE pop: a
    /// cell bursting takes its neighbours with it, so what the ear hears as a single crackle is a
    /// short burst. 34 ms of window inside a 55 ms buffer, which leaves the last pop room to
    /// decay.</summary>
    private const float CrackleSeconds = 0.055f;
    private const float CrackleFirst = 0.0004f;
    private const float CrackleLast = 0.034f;

    /// <summary>How many pops each of the four realisations has. Three to five: below three it is a
    /// tick, above five it is a rattle.</summary>
    private static readonly int[] CracklePops = { 3, 4, 5, 4 };

    /// <summary>The burst's gaps WIDEN slightly (&gt; 1) and are heavily jittered. A cascade of
    /// bursting cells starts fast and thins out, and it has no rhythm at all — the same shape as the
    /// bookshelf's scatter, for the same reason and through the same
    /// <see cref="EnvSoundSchedule.SlipTrain"/>, so this loop TERMINATES BY CONSTRUCTION.</summary>
    private const float CrackleSpread = 1.25f;
    private const float CrackleJitter = 0.75f;

    /// <summary>Decay of one pop, and the power law on the sizes of all but the first. 0.35 ms is
    /// 60 dB down in 2.4 ms — a pressure step, not a click with a tail. The exponent is the drip
    /// variants' and the shelf scatter's argument again: cells are not all the same size and the
    /// small ones vastly outnumber the large, and <c>u^1.5</c> for uniform <c>u</c> is the inverse
    /// CDF that says so. The FIRST pop is forced to full size (it is the one that set the cascade
    /// off) so that the burst has a leading edge and the buffer peaks on it.</summary>
    private const float CrackleTau = 0.00035f;
    private const float CrackleExponent = 1.5f;

    /// <summary>...and each pop after the first is also damped by this to the power of its index. The
    /// power law alone can hand pop four the biggest draw of the burst, which reads as a crackle
    /// running BACKWARDS. 0.62 makes the burst decay whatever the draws do, and leaves the peak on
    /// the first pop, which is what puts the attack at 0.5 ms.</summary>
    private const float CrackleFade = 0.62f;

    /// <summary>THE BAND THAT IDENTIFIES A FIRE, and the whole point of the layer. 900 Hz twice and
    /// 3800 Hz FOUR times — 24 dB/oct off the top, which is what it takes to keep a noise transient
    /// out of 5-24 kHz. Without it 38% of a variant's energy sat above 5 kHz, which on this hardware
    /// is hiss the ear does not reward; with it, every variant puts 71-87% of its energy in 1-5 kHz
    /// where the speaker and the ear are both at their best. The lower corner is at 900 rather than
    /// at 1000 so the crackle keeps a little of the wood under it and does not become a spark.</summary>
    private const float CrackleLoHz = 900f;
    private const float CrackleHiHz = 3800f;
    private const int CrackleLoPoles = 2;
    private const int CrackleHiPoles = 4;

    /// <summary>The four crackles' peaks. NOT EQUAL, and for the drip variants' reason exactly:
    /// <see cref="Normalise"/> sets each buffer's peak independently, so normalising all four to one
    /// number would erase the loudness variation the burst structure built. The spread that survives
    /// is 5.7 dB in the 20 ms window, which is the difference between a cell letting go and a whole
    /// knot going.</summary>
    private static readonly float[] CracklePeaks = { 0.95f, 0.78f, 0.95f, 0.86f };

    /// <summary>One seed per realisation, so the four are genuinely different bursts rather than one
    /// burst at four levels.</summary>
    private static readonly uint[] CrackleSeeds = { 0xC7AC1E00u, 0xC7AC1E01u, 0xC7AC1E02u, 0xC7AC1E03u };

    /// <summary>The four crackles. Fills <see cref="_crackles"/> and returns element 0, which is also
    /// <see cref="Crackle"/>. See THE FIRE above for what a crackle physically is and for the
    /// measured bands.</summary>
    private static AudioClip MakeCrackles(int rate)
    {
        for (int v = 0; v < _crackles.Length; v++)
            _crackles[v] = MakeCrackle(rate, v);
        return _crackles[0]!;
    }

    private static AudioClip MakeCrackle(int rate, int which)
    {
        int n = (int)(rate * CrackleSeconds);
        var d = new float[n];
        uint seed = CrackleSeeds[which];
        var r = new Rng(seed);

        int count = CracklePops[which];
        var pops = new float[count];
        EnvSoundSchedule.SlipTrain(pops, CrackleFirst, CrackleLast,
                                   shrink: CrackleSpread, jitter: CrackleJitter, seed: seed);

        int popLen = (int)(rate * 0.006f);
        for (int k = 0; k < count; k++)
        {
            int at = (int)(pops[k] * rate);
            // The first pop does NOT draw — it is the one that set the cascade off and is full size
            // by construction. Skipping the draw rather than discarding it is what keeps the
            // remaining sequence identical to the replica the table above was measured on.
            float amp = (k == 0 ? 1f : Mathf.Pow(Mathf.Abs(r.Next()), CrackleExponent))
                        * Mathf.Pow(CrackleFade, k);
            for (int i = 0; i < popLen && at + i < n; i++)
            {
                float tt = i / (float)rate;
                d[at + i] += r.Next() * Mathf.Exp(-tt / CrackleTau) * amp;
            }
        }

        for (int p = 0; p < CrackleLoPoles; p++)
            HighPass(d, rate, CrackleLoHz);
        for (int p = 0; p < CrackleHiPoles; p++)
            LowPass(d, rate, CrackleHiHz);

        Normalise(d, CracklePeaks[which]);
        return Finish("Crackle" + which, d, rate);
    }

    /// <summary>Length of one ember settle and the window its thuds land in. Four times the
    /// crackle's, because a lump shifting in a bed of coals is not one contact — it tips, drops and
    /// beds itself, over a tenth of a second or so.</summary>
    private const float EmberSeconds = 0.22f;
    private const float EmberFirst = 0.001f;
    private const float EmberLast = 0.115f;

    /// <summary>Thuds per realisation, and the same widening, heavily jittered train the crackle
    /// uses — a lump coming to rest slows down.</summary>
    private static readonly int[] EmberTicks = { 4, 3 };
    private const float EmberSpread = 1.45f;
    private const float EmberJitter = 0.65f;

    /// <summary>Decay of one thud, the power law on the sizes after the first, and the damping across
    /// the train. Thirteen times the crackle's time constant: charcoal is not a pressure step, it is
    /// a soft body arriving on other soft bodies.</summary>
    private const float EmberTau = 0.0045f;
    private const float EmberExponent = 1.3f;
    private const float EmberFade = 0.70f;

    /// <summary>The lump's own two modes, and how hard they are damped. Q = 6 is 1.9 cycles, which is
    /// under the ~4 the ear needs to extract a pitch — the same test <c>FallBoardQ</c> and
    /// <c>FallCarcassQ</c> are held to. It gives the settle a BODY and no note, which is what
    /// separates a lump of charcoal from a woodblock.</summary>
    private static readonly float[] EmberRingHz = { 620f, 980f };
    private const float EmberRingQ = 6f;
    private const float EmberRingMix = 0.45f;

    /// <summary>The settle's band — DULLER than the crackle by two octaves at the top, which is the
    /// whole of what "duller" means here: centroid 1640 Hz against the crackle's 1856-3175. Still
    /// clear of the hardware's dead zone (0.1-0.2% under 200 Hz), because a settle nobody can hear is
    /// not restraint, it is a missing layer.</summary>
    private const float EmberLoHz = 500f;
    private const float EmberHiHz = 2600f;
    private const int EmberLoPoles = 2;
    private const int EmberHiPoles = 2;

    /// <summary>The settle's peak, against the crackle's 0.78-0.95. It is BELOW the crackle in peak
    /// and slightly ABOVE it in the 20 ms window (0.117 against 0.109), which is exactly what a
    /// longer, softer event should measure; <c>EnvSound.FireEmberLevel</c> is where the two are
    /// finally balanced against each other.</summary>
    private const float EmberPeak = 0.55f;

    private static readonly uint[] EmberSeeds = { 0xE0BE0000u, 0xE0BE0001u };

    /// <summary>The two ember settles. Fills <see cref="_embers"/> and returns element 0, which is
    /// also <see cref="Ember"/>.</summary>
    private static AudioClip MakeEmbers(int rate)
    {
        for (int v = 0; v < _embers.Length; v++)
            _embers[v] = MakeEmber(rate, v);
        return _embers[0]!;
    }

    private static AudioClip MakeEmber(int rate, int which)
    {
        int n = (int)(rate * EmberSeconds);
        var d = new float[n];
        uint seed = EmberSeeds[which];
        var r = new Rng(seed);

        int count = EmberTicks[which];
        var ticks = new float[count];
        EnvSoundSchedule.SlipTrain(ticks, EmberFirst, EmberLast,
                                   shrink: EmberSpread, jitter: EmberJitter, seed: seed);

        int tickLen = (int)(rate * 0.05f);
        for (int k = 0; k < count; k++)
        {
            int at = (int)(ticks[k] * rate);
            float amp = (k == 0 ? 1f : Mathf.Pow(Mathf.Abs(r.Next()), EmberExponent))
                        * Mathf.Pow(EmberFade, k);
            float f = EmberRingHz[k % EmberRingHz.Length];
            float aRing = Mathf.PI * f / EmberRingQ;
            for (int i = 0; i < tickLen && at + i < n; i++)
            {
                float tt = i / (float)rate;
                d[at + i] += (r.Next() * Mathf.Exp(-tt / EmberTau)
                              + Mathf.Sin(2f * Mathf.PI * f * tt) * Mathf.Exp(-aRing * tt) * EmberRingMix)
                             * amp;
            }
        }

        for (int p = 0; p < EmberLoPoles; p++)
            HighPass(d, rate, EmberLoHz);
        for (int p = 0; p < EmberHiPoles; p++)
            LowPass(d, rate, EmberHiHz);

        Normalise(d, EmberPeak);
        return Finish("Ember" + which, d, rate);
    }

    // =============================================================================================
    //  THE CANDLE — ModBuild 153, and it is a DELETION of a wind as much as an addition of a flame.
    // =============================================================================================
    //
    //  USER REPORT, ModBuild 152 hardware, verbatim:
    //
    //      "Beim Feuer Geräusch ist auch immer das Wind geräusch mit dabei. Das soll nicht sein.
    //       Das Wind gEräusch soll nur dann kommen wenn Wind auch aktiv ist."
    //
    //  He is restating a ruling he already gave at ModBuild 147 ("Wind Geräusch nur wenn auch Wind
    //  aktiv ist, sonst kein Geräusch") and which ModBuild 148 believed it had implemented. It had
    //  not, and the reason is in one line of EnvSound.BuildCellar rather than in his ears:
    //
    //      AddBed($"Flame{candles}", t, EnvSoundBank.Bank(EnvSoundClip.Bed), 0.055f, ...,
    //             () => 0.72f + 0.28f * Lfo(3.11f) + 0.9f * ElementMood.Live(0));
    //
    //  THREE SEPARATE FAULTS IN ONE CALL, and all three are the same mistake — a sound borrowing a
    //  convenient noise buffer:
    //    1. EnvSoundClip.Bed IS THE WIND. It is the buffer the window Draught and the swamp Leaves
    //       play; its own doc comment says so. So the cellar's three candle groups have been playing
    //       the draught since the feature shipped.
    //    2. THEY WERE NOT AIR-GATED. Draught and Leaves go through WindBed(), which is hard-zeroed by
    //       _windGate; the Flame beds did not, so the wind buffer was audible in the cellar with Air
    //       fully off — the 147 ruling broken on its face.
    //    3. `+ 0.9f * ElementMood.Live(0)` IS FIRE. Infusing Fire made the WIND CLIP louder, by a
    //       measured +6.2 dB on each of the room's three candle beds. That is, literally, "beim Feuer
    //       Geräusch ist auch immer das Wind Geräusch mit dabei".
    //  ModBuild 152 added the seated fires' own Roar/Crackle/Ember beside this call and LEFT IT
    //  STANDING, which is why the report survived that round.
    //
    //  WHAT A CANDLE ACTUALLY IS, because the fix is not "quieter wind". A candle flame is 15-30 mm
    //  of laminar-to-barely-turbulent combustion. Three consequences, and each one is a constant
    //  below:
    //    * IT HAS NO LOW END AT ALL. A radiator that small cannot move air at 100 Hz; the draught's
    //      body — 53.2% of the shared bed's energy is under 200 Hz — is a property of a window-sized
    //      APERTURE and a candle has no equivalent. FlutterLoHz.
    //    * IT IS NARROW-BAND. A draught is a broadband rush (measured spread 1.10 octaves); a small
    //      flame is a band of noise around its own eddy scale (0.66 octaves). This is the strongest
    //      spectral separation available and it is what "narrow-band flutter" means as a number.
    //    * IT FLUTTERS FASTER THAN A FIRE. The instability rate goes as 1/sqrt(size), so a candle
    //      guts several times a second where a burning crate puffs at RoarPuffHz = 5.5. FlutterHz is
    //      11 — twice the fire's, and deliberately under 20 Hz, above which amplitude modulation
    //      stops being heard as flutter and starts being heard as roughness.
    //  ...and the WICK, which is the only part of a candle that makes a discrete sound: a trapped
    //  impurity or a bead of wax spitting. FlutterTicks.
    //
    //  MEASURED, off the finished buffers by the replica in .planning/envsound-replica/candle.py.
    //  "Draught" is the shared bed THROUGH ITS RUNTIME 1150 Hz LOW PASS, i.e. what the player is
    //  actually handed, because comparing raw buffers would compare something nobody hears.
    //
    //    energy by band     0-200  200-500  500-1k    1-2k    2-5k   5-24k   centroid  spread(>200Hz)
    //    Draught (Bed)      53.2%    22.0%   13.2%    7.9%    3.2%    0.5%     441 Hz     1.10 oct
    //    Candle (Flutter)    1.1%    26.7%   54.5%   16.8%    0.9%    0.0%     728 Hz     0.66 oct
    //    Roar                11.4%   56.2%   28.8%    3.5%    0.1%    0.0%     446 Hz     0.64 oct
    //
    //    envelope (20 ms)   sigma/mean   5-95% swing   mod centroid   1-8 Hz share
    //    Draught (Bed)           0.150       4.02 dB       18.13 Hz          23.3%
    //    Candle (Flutter)        0.247       7.46 dB        7.80 Hz          56.0%
    //    Roar                    0.257       7.47 dB        7.44 Hz          61.2%
    //
    //  THE TWO NUMBERS THAT SETTLE "DOES IT STILL READ AS WIND":
    //    * 0-200 Hz: 1.1% against 53.2%. The draught's whole body is in a band the candle does not
    //      occupy at all — a 48x ratio, and it is the physics rather than a filter choice.
    //    * SPREAD: 0.66 octaves against 1.10. The candle is a BAND and the draught is a RUSH. The
    //      centroid alone would NOT have settled it (728 vs 441 raw, 660 vs 598 over the audible
    //      band only) because the draught's own centroid is dragged up by a hiss tail; that is
    //      exactly the measurement that would have been quoted if only one had been taken.
    //
    //  AND THE LEVEL IS NOT PART OF THE CHANGE. At EnvSound's unchanged 0.055 gain, the rebuilt bed
    //  measures 1.76 dB QUIETER than the wind buffer it replaces (RMS through a 200 Hz high pass,
    //  the headset stand-in). It also puts 5.3 dB LESS absolute energy into the 1-5 kHz band the
    //  class doc protects than the draught bed does — so the narrower, higher band is not bought
    //  with any of the "never mask" budget. Both are measured, not argued.
    //
    //  REJECTED:
    //    * KEEPING Bed AND FILTERING IT HARDER AT RUNTIME. The runtime AudioLowPassFilter is ONE
    //      pole; carving a 470-1050 Hz band out of a buffer whose energy is 53% below 200 Hz would
    //      need a high pass this feature has no runtime component for, and would leave the candle
    //      playing the draught's own samples — perfectly correlated with the window bed three metres
    //      away, which is the correlation AddBed's start offset exists to prevent.
    //    * DERIVING IT FROM Roar AT A LOWER LEVEL. Weighed seriously, since it needs no new buffer.
    //      It fails the measurement: the roar's audible centroid is 436 Hz and its spread 0.64
    //      octaves — it is DARKER than the draught, not brighter, so a quiet roar under the candles
    //      would have been a second low rush in the same room and the report would have survived
    //      another round.
    //    * SCHEDULING THE WICK TICKS AS ONE-SHOTS, the way TickFire schedules crackles. It is the
    //      technically purer answer (nothing recurs with the buffer) and it was rejected on cost: it
    //      needs a per-site scheduler, three more Voice references, teardown state and its own wire
    //      vectors, for a sound 16 dB under a crackle that only plays when the player is leaning
    //      over the candles. The ticks are baked instead, and the loop argument is answered by
    //      MEASUREMENT rather than by construction — see FlutterTickMix.

    /// <summary>Length of the flutter loop. SEVEN seconds, against the shared bed's eight and the
    /// roar's six: the three continuous buffers in a room are then mutually prime in whole seconds,
    /// so no two of their wraps coincide inside three minutes. (They also never play in the same
    /// room as each other at full level — but "nothing loops at a period the ear can find" is item 6
    /// of the class doc and a free property is worth taking.)</summary>
    private const float FlutterSeconds = 7f;

    /// <summary>THE CANDLE'S BAND, and it is the whole of what separates this clip from the draught.
    /// Two poles at 470 Hz off the bottom and FOUR at 1050 off the top.
    ///
    /// <para>THE FLOOR IS PHYSICS, not the hardware for once: a 20 mm flame does not radiate at
    /// 100 Hz, so the 53.2% of the draught's energy that lives under 200 Hz has no counterpart here
    /// and the measured figure is 1.1%. THE CEILING IS THE CLASS DOC — this is a CONTINUOUS layer, so
    /// it is held under the 1-4 kHz band where speech and the game's UI cues live. Four poles rather
    /// than the roar's three because the corner is higher and the band has to close before 2 kHz:
    /// the finished buffer measures 0.9% above 2 kHz.</para>
    ///
    /// <para>AND THE RUNTIME FILTER IS TURNED OFF FOR THIS BED (EnvSound.AddBed's <c>lowPassHz: 0</c>,
    /// the same call the fire makes), which is a tightening rather than a relaxation: the runtime
    /// filter is ONE pole at 1150 Hz, i.e. -6 dB/oct, while this is -24 dB/oct from 1050. At 3 kHz
    /// the runtime filter gives -8.6 dB and this gives -23 dB. Nothing is given up in the band the
    /// class doc protects, and three AudioLowPassFilter components stop being evaluated per DSP
    /// block.</para></summary>
    private const float FlutterLoHz = 470f;
    private const float FlutterHiHz = 1050f;
    private const int FlutterLoPoles = 2;
    private const int FlutterHiPoles = 4;

    /// <summary>The flutter rate. TWICE the fire's <see cref="RoarPuffHz"/>, because the instability
    /// that drives both scales as 1/sqrt(size) and a candle flame is two orders of magnitude smaller
    /// than a burning crate. It is not scaled the whole way — the law would give some 35 Hz — and the
    /// reason is a bound rather than taste: above roughly 20 Hz an amplitude modulation stops being
    /// heard as a FLUTTER and starts being heard as roughness, i.e. as a timbre of the noise rather
    /// than as something moving. 11 Hz keeps it visibly the same phenomenon as the fire's puff, twice
    /// as fast, which is what a candle beside a fire should be.</summary>
    private const float FlutterHz = 11f;

    /// <summary>How far the flutter may pull the bed down, and over how many sigma of the envelope
    /// stream that range is spanned. Both mirror <see cref="RoarPuffFloor"/>/<see cref="RoarPuffSigmas"/>
    /// and the second one exists for the reason set out there — a peak-normalised envelope delivers
    /// about half the swing its floor claims.
    ///
    /// <para>0.38 against the fire's 0.42: a candle is a smaller flame with less momentum, so it
    /// guts deeper relative to itself. Measured on the finished buffer that is a 5-95% swing of
    /// 7.46 dB with 56.0% of the modulation between 1 and 8 Hz, against the draught's 4.02 dB at a
    /// modulation centroid of 18 Hz — which is the number that says the draught's "envelope" is
    /// nothing but the fluctuation of stationary noise, and this one is a flame.</para></summary>
    private const float FlutterFloor = 0.38f;
    private const float FlutterSigmas = 1.8f;

    /// <summary>THE WICK. How many sputters are laid into the buffer and the window they land in —
    /// an impurity in the braid or a bead of wax reaching the flame, which is the only discrete
    /// sound a candle makes. The train is <see cref="EnvSoundSchedule.SlipTrain"/>'s, at shrink 1
    /// (no drift either way — a wick has no cascade) and a heavy jitter, so the spacing is irregular
    /// and the loop TERMINATES BY CONSTRUCTION like every other caller of it.</summary>
    private const int FlutterTicks = 18;
    private const float FlutterTickFirst = 0.08f;
    private const float FlutterTickLast = 6.90f;
    private const float FlutterTickSpread = 1f;
    private const float FlutterTickJitter = 0.90f;

    /// <summary>One sputter's decay, the power law on its size, and how loud the whole layer is
    /// against the flutter.
    ///
    /// <para><b>THE MIX IS WHERE THE LOOP ARGUMENT IS SETTLED, and it is settled by measurement.</b>
    /// Item 6 of the class doc forbids a recognisable EVENT inside a looping buffer — the ear finds
    /// it and from then on the bed announces its own period. So the ticks were measured against the
    /// flutter's own 4 ms level: at this mix the MEDIAN sputter is +3.2 dB over it (i.e. inside the
    /// noise's own fluctuation and not an event at all) and only four of the eighteen exceed +5 dB,
    /// the loudest reaching +8.2 dB. The buffer's crest factor is 15.2 dB against the flutter's own
    /// 14.4 dB — the sputters barely touch the peak, which matters because the peak is what
    /// <see cref="Normalise"/> sets the WHOLE BED's level from: a loud tick would buy itself by
    /// making every candle in the room quieter. What recurs every 7 s is therefore a
    /// TEXTURE, and the three candle groups start at different offsets in it (EnvSound.AddBed) and
    /// run their own gain LFOs on top.</para>
    ///
    /// <para>0.8 ms of decay: a wick tick is a pressure step and not a click with a tail. The
    /// exponent is the crackle's and the drip's and the shelf scatter's — <c>u^1.5</c> is the inverse
    /// CDF of a size distribution where the small vastly outnumber the large, which is what a wick
    /// spits. The ticks go through the same band filters as the flutter, deliberately: a wick sputter
    /// is a dull little pop from inside the same small flame, not a spark.</para></summary>
    private const float FlutterTickTau = 0.0008f;
    private const float FlutterTickExponent = 1.5f;
    private const float FlutterTickMix = 2f;

    /// <summary>The flutter's peak. Chosen so that the REBUILT bed lands within about 2 dB of the
    /// level the wind buffer gave the candles at the same 0.055 gain — measured 1.76 dB quieter
    /// through a 200 Hz high pass. This round changes what the candles sound like and deliberately
    /// not how loud they are: a rebuild that also moved the level would leave the next hardware
    /// report unable to say which of the two it was judging. Where it is not exactly level it errs
    /// QUIET, which is this file's standing bias.</summary>
    private const float FlutterPeak = 0.70f;

    private const uint FlutterSeed = 0xCA9D1E00u;

    /// <summary>
    /// THE CANDLE FLAME. Narrow-band noise inside the flame's own eddy band, fluttering at
    /// <see cref="FlutterHz"/>, with the wick's sputters laid in. See THE CANDLE above for the user
    /// report that produced it, the physics and the measurements.
    ///
    /// <para>TWO PASSES OVER ONE BUFFER, exactly as <see cref="MakeRoar"/> does it and for the same
    /// reason: the envelope has to be normalised before it can be applied, and writing it into
    /// <c>d</c> saves a second buffer inside a bank build that runs on the frame the room is
    /// placed.</para>
    /// </summary>
    private static AudioClip MakeFlutter(int rate)
    {
        int n = (int)(rate * FlutterSeconds);
        var d = new float[n];

        // ---- pass 1: THE ENVELOPE, into d, and its sigma. See RoarPuffSigmas.
        var er = new Rng(FlutterSeed ^ 0x5A5A5A5Au);
        float ke = 1f - Mathf.Exp(-2f * Mathf.PI * FlutterHz / rate);
        float e1 = 0f, e2 = 0f;
        double acc = 0.0;
        for (int i = 0; i < n; i++)
        {
            float v = er.Next();
            e1 += ke * (v - e1);
            e2 += ke * (e1 - e2);
            d[i] = e2;
            acc += (double)e2 * e2;
        }
        float span = FlutterSigmas * (float)System.Math.Sqrt(acc / Mathf.Max(n, 1));
        if (!(span > 1e-9f))
            span = 1f;

        // ---- pass 2: a WHITE carrier times that envelope. White and not pink: the band filters
        // below are four poles wide at the top and two at the bottom, so the shape inside the band
        // is set by them and a pink tilt underneath would only push the centroid back down toward
        // the draught's — which is the one thing this clip exists not to do.
        var r = new Rng(FlutterSeed);
        for (int i = 0; i < n; i++)
        {
            float u = Mathf.Clamp01(0.5f * (1f + d[i] / span));
            d[i] = r.Next() * (FlutterFloor + (1f - FlutterFloor) * u);
        }

        // ---- the wick.
        var ticks = new float[FlutterTicks];
        EnvSoundSchedule.SlipTrain(ticks, FlutterTickFirst, FlutterTickLast,
                                   shrink: FlutterTickSpread, jitter: FlutterTickJitter,
                                   seed: FlutterSeed);
        int tickLen = (int)(rate * 0.004f);
        var tr = new Rng(FlutterSeed ^ 0x7C1C0000u);
        for (int k = 0; k < FlutterTicks; k++)
        {
            int at = (int)(ticks[k] * rate);
            float amp = Mathf.Pow(Mathf.Abs(tr.Next()), FlutterTickExponent) * FlutterTickMix;
            for (int i = 0; i < tickLen && at + i < n; i++)
            {
                float tt = i / (float)rate;
                d[at + i] += tr.Next() * Mathf.Exp(-tt / FlutterTickTau) * amp;
            }
        }

        for (int p = 0; p < FlutterLoPoles; p++)
            HighPass(d, rate, FlutterLoHz);
        for (int p = 0; p < FlutterHiPoles; p++)
            LowPass(d, rate, FlutterHiHz);

        LoopFade(d, rate / 2);
        Normalise(d, FlutterPeak);
        return Finish("Flutter", d, rate);
    }


    // ---- one-shots ------------------------------------------------------------------------------

    // =============================================================================================
    //  THE DRIP — rebuilt from the physics up. ModBuild 147.
    // =============================================================================================
    //
    //  THE USER REPORT, verbatim: "Die Pfütze ist zu extrem bzw. reagiert zu extrem den
    //  Wassertropfen. Außerdem gefällt mir das Geräusch nicht." The first sentence is the PUDDLE's
    //  reaction and belongs to the shader; this is the second sentence, and only the second.
    //
    //  WHAT THE OLD CLIP ACTUALLY WAS, in the units the numbers mean rather than in the numbers.
    //  Minnaert's relation for an air bubble in water at one atmosphere is
    //
    //      f0 = (1 / 2*pi*r) * sqrt(3*gamma*P0 / rho)   ~=   3.26 / r      (Hz with r in metres)
    //
    //  so a frequency IS a bubble radius and nothing else. Read that way, the shipped clip said:
    //    * F0 = 720 Hz          -> a 4.5 mm bubble. Plausible on its own.
    //    * Rise = 2.35          -> ...which then climbs to 1692 Hz, i.e. shrinks to a 1.9 mm bubble.
    //                              That is 42% of the radius, so SEVEN PER CENT of the volume, and
    //                              the clip claims it happens inside ~120 ms. Nothing does that. A
    //                              real drip's Minnaert tone rises by some tens of per cent over its
    //                              life (the bubble loses a little gas and drifts toward the free
    //                              surface, which lightens the load on it); it does not sweep an
    //                              octave and a fifth. An octave-and-a-fifth glide on a pure sine is
    //                              the definition of a synthesizer patch, and it is precisely what
    //                              the ear was reporting as "a bell".
    //    * Decay = 17           -> a 59 ms time constant, so the tone was still at -30 dB after a
    //                              fifth of a second. It RANG. It rang, in a room, every 2.85 s.
    //    * 6 ms of noise at 0.55 -> and this was the entire impact. Six milliseconds of broadband is
    //                              a CLICK, not a splash; it reads as the attack transient of the
    //                              tone that follows it, which is exactly how a struck bell is put
    //                              together. So the clip had the bell's envelope AND the bell's
    //                              spectrum, and the one thing it had almost none of is the thing a
    //                              drop landing in a puddle mostly IS.
    //    * HighPass 180 + Normalise 0.9 -> nothing removed above, everything pushed to the ceiling.
    //
    //  ...and one structural error underneath all of that: the sine started at FULL AMPLITUDE at
    //  t = 0, simultaneous with the transient. The bubble does not exist at the moment of impact.
    //  The drop has to open a crater first, and the crater has to pinch off behind it; only then is
    //  there a bubble to ring. That pinch-off is milliseconds AFTER contact. Starting the two
    //  together fuses them into one event with an attack and a tail — which the ear names "bell".
    //  Separating them by ~9 ms is most of what makes the same two ingredients name "plop" instead.
    //
    //  WHAT THIS ROOM'S DROP ACTUALLY IS. The content lane's own numbers say it: the drop forms on a
    //  plank at DripY0 = 3.252 and lands on water at DripY1 = 0.008 (BuildEnvironmentRooms.cs
    //  :1705-6, mirrored in EnvSound.cs), so it falls 3.244 m in free fall and arrives at
    //  sqrt(2 * 9.81 * 3.244) = 7.98 m/s. That is fast, and it lands in a PUDDLE — a film of water a
    //  few millimetres deep lying on a flagstone. Three consequences, and each one is a term below:
    //
    //    1. THE FLOOR STOPS IT, not the water. Eight metres per second of drop is arrested inside a
    //       couple of millimetres, against stone. Most of the energy leaves as a broadband SLAP
    //       (water thrown sideways, ~10-20 ms, no clean pitch) plus a very short low knock from the
    //       flagstone itself. The old clip's 6 ms tick was standing in for this and was far too
    //       brief and far too bright to do it.
    //    2. THE CAVITY IS WIDE AND SHALLOW, because it bottoms out on the stone before it can go
    //       deep. A wide shallow cavity pinches off a BIG bubble, and a big bubble is a LOW one:
    //       ~430 Hz is a 7.6 mm bubble, which is what a puddle of this depth plausibly entrains. The
    //       shipped 720 -> 1692 Hz was the sound of a deep, narrow entrainment — a sink, a bucket,
    //       a cave pool — not of 3 mm of water on a cellar floor.
    //    3. IT DIES FAST. A bubble oscillating within a few millimetres of a rigid floor AND within
    //       a few millimetres of the free surface loses energy into both. See BubbleQ.
    //
    //  REJECTED, in order of how tempting they were:
    //    * SCALING THE EXISTING CONSTANTS (F0 down, Decay up, done). It would not have worked, and
    //      that is a statement about the shape rather than about the taste: the clip's two real
    //      defects are the MISSING slap and the SIMULTANEITY of impact and ring, and no multiplier
    //      on F0/Rise/Decay reaches either. A quieter, lower bell is still a bell.
    //    * A CC0 RECORDING. The class doc weighs this in general; for THIS clip it is also the wrong
    //      answer specifically. A drip is one of the few sounds that is genuinely a small number of
    //      resonances plus an impact, so the arithmetic is an honest model rather than an imitation
    //      — and a recorded drip brings a recorded ROOM with it, which would then be heard inside
    //      our room. (The squeak remains the one clip worth replacing; see the class doc.)
    //    * SYNTHESIZING PER SHOT so every drop is unique. It would have to run on the audio thread
    //      or allocate a buffer per event, both of which this feature is built not to do. Three
    //      baked realisations cost 84 kB once and nothing per shot.
    //    * WIDER PITCH JITTER instead of the three realisations. Rejected on physics: the playback
    //      pitch knob shifts the WHOLE clip, so it transposes the flagstone and the slap along with
    //      the bubble — and the floor does not change note between drops. It is also the most
    //      recognisable synthetic-audio tell there is; the ear hears "one sample, transposed" within
    //      three or four repeats, which at a 2.85 s period is under fifteen seconds.
    //    * DELETING THE DRIP SOUND. Considered seriously under "dezent", and rejected: the picture
    //      shows a visible drop hitting a visible puddle every 2.85 s, and a visible impact with no
    //      sound reads as a bug rather than as restraint. The answer to "too noticeable" is small,
    //      not absent — see the level note in EnvSound.TickDrip.

    /// <summary>
    /// The three drops. Fills <see cref="_drips"/> and returns element 0, which is also
    /// <see cref="Drip"/>.
    ///
    /// <para><b>WHY THREE, and why they differ in TIMBRE rather than in pitch.</b> Successive drops
    /// off the same plank are almost identical in mass and in fall height — the one thing that is
    /// genuinely different each time is WHAT THE SURFACE DOES, because the surface is still moving
    /// from the last drop and the puddle is not the same depth in two places. The visible
    /// consequence of that is the bubble: sometimes the cavity pinches off a big one, sometimes a
    /// smaller one, and OFTEN IT PINCHES OFF NOTHING AT ALL and the drop is only its impact. That
    /// last case is not a degenerate variant, it is the common one in shallow water, and it is the
    /// single most useful thing in this whole change for the standing "dezent" rule: one drop in
    /// three now has no tone in it whatsoever, so there is nothing for the ear to latch onto and
    /// count.</para>
    ///
    /// <para><b>THE PEAKS ARE NOT EQUAL, and that is load-bearing.</b> <see cref="Normalise"/> sets
    /// each clip's peak independently, so if all three were normalised to the same number the
    /// quiet variant would come back at exactly the loudness of the loud one and the variation
    /// built above would be erased on the last line of the generator. The peaks below therefore
    /// carry the RELATIVE loudness of the three realisations explicitly: 0.78 / 0.52 / 0.70.</para>
    ///
    /// <para><b>MEASURED, off the finished buffers</b> (the generator was run outside Unity against
    /// the same arithmetic, because none of this is checkable by ear from a build machine):</para>
    /// <code>
    ///   variant     peak at   audible to -40 dB   RMS      impact leads ring by
    ///   ordinary     3.8 ms        126 ms         0.105          +2.4 dB
    ///   no bubble    0.9 ms         57 ms         0.046         +10.5 dB
    ///   small        1.5 ms         87 ms         0.075          +3.6 dB
    ///   SHIPPED     15   ms        244 ms         0.110       ring LOUDER than impact
    /// </code>
    /// <para>Three things in that table are the whole change: every variant now PEAKS IN ITS FIRST
    /// FOUR MILLISECONDS (the old clip peaked at 15 ms, on the tone), every variant is audible for
    /// well under half as long, and the average RMS across the three is 0.075 against 0.110 —
    /// -3.3 dB of loudness before the level in <c>EnvSound.TickDrip</c> is touched at all.</para>
    ///
    /// <para><b>COST.</b> 3 x 0.28 s where there was 1 x 0.40 s: 40 320 samples against 19 200, so
    /// ~84 kB more PCM against the bank's ~2 MB, and roughly two more milliseconds of synthesis
    /// against the bank's measured 112 ms. Per sample this is cheaper than the bed (one sin and two
    /// exp against three filter poles), and the drip is 0.8 s of the bank's ~26 s of audio either
    /// way. The log line in <see cref="Build"/> reports the real figure.</para>
    ///
    /// <para><b>TERMINATION.</b> Nothing here has one to prove. Every loop is a <c>for</c> over
    /// <c>n = (int)(rate * seconds)</c>, an int fixed before the loop starts; there is no float
    /// accumulator in any condition and no burst train (so no <see cref="EnvSoundSchedule"/> call
    /// and nothing new for the wire vectors to hold). This is the file ModBuild 145 froze the game
    /// in — see <see cref="EnvSoundSchedule"/> — so the property is stated rather than assumed.</para>
    /// </summary>
    private static AudioClip MakeDrips(int rate)
    {
        // VARIANT 0 — THE ORDINARY DROP. A wide shallow cavity pinches off a 7.6 mm bubble.
        //
        // THE ONE BALANCE THAT DECIDES WHETHER THIS IS A PLOP OR A PLINK: the loudest instant in the
        // buffer has to be the IMPACT, not the ring. It is a measurable property, not a taste — walk
        // the finished buffer and ask where its peak is. At bubbleAmp 0.30 the peak lands at 15 ms,
        // i.e. on the bubble, and the clip is a struck tone with a bit of noise in front of it (which
        // is exactly what shipped). At 0.20 the peak moves to 3.8 ms and the ring sits 2.4 dB under
        // it: the impact leads and the tone colours it. That is the whole difference, and it is worth
        // re-measuring rather than re-tuning if anyone ever touches slapAmp or stoneAmp.
        _drips[0] = MakeDripVariant(rate, "Drip", 0xD819u,
                                    bubbleHz: 430f, bubbleAmp: 0.20f, bubbleRise: 1.16f,
                                    slapHz: 1500f, slapTau: 0.0045f, slapAmp: 0.85f,
                                    stoneHz: 168f, stoneTau: 0.011f, stoneAmp: 0.22f,
                                    peak: 0.78f);

        // VARIANT 1 — NO BUBBLE. The crater collapsed without closing over, so there is no
        // oscillator and no tone at all: an impact and the flagstone under it, nothing else. More of
        // the momentum reached the stone (stoneAmp up, and a slightly lower, longer knock because it
        // is a squarer hit), and the whole event is quieter — a drop that fails to entrain has spent
        // its energy on spray, which radiates poorly. THE ONE THAT MAKES THE DRIP STOP BEING AN
        // EVENT: no pitch means nothing to recognise, and a third of all drops now have none.
        _drips[1] = MakeDripVariant(rate, "Drip.NoBubble", 0xD81Au,
                                    bubbleHz: 0f, bubbleAmp: 0f, bubbleRise: 1f,
                                    slapHz: 1250f, slapTau: 0.0040f, slapAmp: 0.85f,
                                    stoneHz: 152f, stoneTau: 0.013f, stoneAmp: 0.30f,
                                    peak: 0.52f);

        // VARIANT 2 — A SMALLER BUBBLE, 5.3 mm: the drop caught a thinner part of the film. Higher,
        // and by BubbleQ's constant-Q argument also shorter in absolute time. Quieter tone too — a
        // bubble is a monopole, so its output goes with the volume it displaces. 0.15 against 0.20
        // is a far gentler ratio than the (7.6/5.3)^3 = 2.9 that would imply, and deliberately: the
        // two bubbles are not the same bubble at two sizes, they are two different collapses, and the
        // one that pinched off a small bubble put more of the crater's energy into it. Taking the
        // cube law literally would make this variant's tone inaudible, which is not variation, it is
        // a second copy of variant 1.
        _drips[2] = MakeDripVariant(rate, "Drip.Small", 0xD81Bu,
                                    bubbleHz: 620f, bubbleAmp: 0.15f, bubbleRise: 1.22f,
                                    slapHz: 1700f, slapTau: 0.0042f, slapAmp: 0.80f,
                                    stoneHz: 175f, stoneTau: 0.010f, stoneAmp: 0.20f,
                                    peak: 0.70f);

        return _drips[0]!;
    }

    /// <summary>Seconds between the impact and the bubble's first ring: the crater has to open and
    /// its neck has to close behind the drop before there is anything to oscillate. Milliseconds,
    /// but the ear reads THIS gap as the difference between a wet event and a struck one — see the
    /// block comment above on why the old clip started both at t = 0.</summary>
    private const float DripRingDelay = 0.009f;

    /// <summary>
    /// The bubble's quality factor, and the ONE number the three decay rates are derived from
    /// instead of being three separate tunings.
    ///
    /// <para>Minnaert says <c>f * r</c> is a constant (~3.26 Hz.m), so <c>omega * r</c> is a
    /// constant too, so a bubble's RADIATION Q — which is <c>c_water / (omega * r)</c> — is the same
    /// ~72 for every bubble size. Its decay rate <c>alpha = pi * f / Q</c> therefore scales with its
    /// frequency: a higher bubble necessarily dies sooner in absolute time, and that relationship is
    /// not a taste decision to be re-made per variant. 36 is half the free-field radiation limit,
    /// which is what a rigid floor two millimetres below and a free surface two millimetres above
    /// cost it — the shallow puddle damps its own bubbles, which is the third consequence in the
    /// block comment. It gives the 430 Hz drop alpha = 37.5 (a 27 ms time constant, inaudible past
    /// ~150 ms) against the shipped 17 (59 ms, still ringing at 250 ms).</para>
    /// </summary>
    private const float DripBubbleQ = 36f;

    /// <summary>How fast the Minnaert tone climbs, per second. 26 puts the rise's time constant at
    /// 38 ms — the same order as the ring's own decay, i.e. the tone is still bending when it dies,
    /// which is what a rise sounds like as opposed to a glide that arrives somewhere.</summary>
    private const float DripRiseRate = 26f;

    /// <summary>
    /// One realisation of a drop: a SLAP, a flagstone KNOCK, and (optionally) a Minnaert BUBBLE that
    /// starts <see cref="DripRingDelay"/> after the other two.
    ///
    /// <para>Twelve parameters, named at both call sites, because the alternative is three
    /// near-copies of this function differing in constants — and the last time this file had three
    /// near-copies of a schedule, one of them froze the game.</para>
    /// </summary>
    /// <param name="bubbleHz">Minnaert frequency, i.e. bubble radius by another name (r ~ 3.26/f).
    /// ZERO means no bubble was entrained and the drop is only its impact.</param>
    /// <param name="bubbleAmp">Monopole strength — how much volume the bubble displaces. THE
    /// SENSITIVE ONE: it decides whether the finished buffer peaks on the impact or on the ring, and
    /// therefore whether the clip is a plop or a plink. See the note on variant 0.</param>
    /// <param name="bubbleRise">Where the tone ends up, as a multiple of where it started. Tens of
    /// per cent. The shipped 2.35 was the bell.</param>
    /// <param name="slapHz">Top of the slap's band. Water thrown sideways off stone is broadband but
    /// not bright — there is no hard edge anywhere in a puddle to make a click.</param>
    /// <param name="slapTau">Time constant of the slap, seconds. ~4 ms, so it is done in ~20 ms.</param>
    /// <param name="slapAmp">Slap level relative to the rest.</param>
    /// <param name="stoneHz">The flagstone's answer to being hit through 2 mm of water. Low.</param>
    /// <param name="stoneTau">...and heavily damped: a bedded flagstone is not a bell either.</param>
    /// <param name="stoneAmp">How much of the drop's momentum reached the stone.</param>
    /// <param name="peak">Normalisation target — the RELATIVE loudness of this realisation against
    /// the other two. See the note in <see cref="MakeDrips"/> on why these differ.</param>
    private static AudioClip MakeDripVariant(int rate, string name, uint seed,
                                             float bubbleHz, float bubbleAmp, float bubbleRise,
                                             float slapHz, float slapTau, float slapAmp,
                                             float stoneHz, float stoneTau, float stoneAmp,
                                             float peak)
    {
        // 0.28 s, and it is a bound rather than a length: the longest thing in here is the 430 Hz
        // bubble at alpha = 37.5, which is 100 dB down by 0.25 s. The buffer ends in silence, so a
        // one-shot needs no fade (see LoopFade's doc for who does).
        int n = (int)(rate * 0.28f);
        var d = new float[n];
        var r = new Rng(seed);

        // alpha = omega / (2Q) = pi*f/Q. Derived, not tuned — see DripBubbleQ.
        float bubbleDecay = Mathf.PI * bubbleHz / DripBubbleQ;

        // The slap's band, as two one-pole trackers run inline over the sample loop the way
        // MakeBreath and MakeDrag do theirs: yHi follows the noise up to slapHz, yLo follows it up
        // to 190 Hz, and the difference is the band between them. Band-passing the noise (rather
        // than the old clip's single high pass at the very end) is what turns a click into a splash
        // — a click is broadband BY DEFINITION, so the way to stop hearing one is to take the
        // extremes off the noise before anything else happens to it.
        float aHi = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * slapHz / rate));
        float aLo = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * 190f / rate));
        float yHi = 0f, yLo = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;

            // ---- 1. THE SLAP. Zero-mean noise, so its envelope may start at 1 without a DC step;
            // an impact IS a discontinuity and does not want an attack. Fed as literal zero once the
            // envelope is spent rather than switching the filters off, because freezing the two
            // accumulators at their last value and continuing to read them would inject exactly the
            // constant offset this band-pass exists to remove.
            float w = t < 0.05f ? r.Next() * slapAmp * Mathf.Exp(-t / slapTau) : 0f;
            yHi += aHi * (w - yHi);
            yLo += aLo * (yHi - yLo);
            float sample = yHi - yLo;

            // ---- 2. THE FLAGSTONE. sin(wt)*exp(-t/tau) is the impulse response of a damped
            // oscillator, which is what a struck slab is; starting at sin(0) = 0 is not a fade, it
            // is the correct phase for something excited by a blow at t = 0.
            if (t < 0.09f)
                sample += Mathf.Sin(2f * Mathf.PI * stoneHz * t) * Mathf.Exp(-t / stoneTau) * stoneAmp;

            // ---- 3. THE BUBBLE, if one was entrained at all.
            float tb = t - DripRingDelay;
            if (bubbleAmp > 0f && tb > 0f)
            {
                // THE PHASE IS THE INTEGRAL OF THE FREQUENCY, and this comment survives the rewrite
                // because the trap it describes has not moved. The instantaneous frequency is
                //     f(tb) = bubbleHz * (1 + (Rise-1) * (1 - exp(-DripRiseRate*tb)))
                // and the naive sin(2*pi*f(tb)*tb) would sweep the phase at roughly TWICE the
                // intended rate — it multiplies the swept frequency by the elapsed time instead of
                // accumulating it — which turns a drip into a rising whistle. So the closed-form
                // integral of f is used instead.
                float phase = 2f * Mathf.PI * bubbleHz *
                              (tb + (bubbleRise - 1f) *
                                    (tb + (Mathf.Exp(-DripRiseRate * tb) - 1f) / DripRiseRate));

                // The bubble is not there yet at tb = 0 either: the neck takes a moment to close, so
                // the mode swells over ~3 ms rather than appearing. Without this the ring has an
                // attack of its own and reads as a second, smaller strike.
                float grow = 1f - Mathf.Exp(-tb * 350f);
                sample += Mathf.Sin(phase) * grow * Mathf.Exp(-bubbleDecay * tb) * bubbleAmp;
            }

            d[i] = sample;
        }

        // THE CEILING, and it is here for EnvSound's non-masking rule rather than for tone: item 2
        // of that class doc reserves roughly 1-4 kHz for speech and the game's UI transients, and
        // the shipped drip put both its click and the top of its glide straight into it. One pole at
        // 2 kHz on the finished buffer makes "there is nothing of ours up there" a property of the
        // clip instead of a property of the parameters above. It costs the tones nothing measurable
        // (620 Hz through it is -0.4 dB).
        LowPass(d, rate, 2000f);

        // ...and the floor, below everything real in here (the lowest knock is 152 Hz): it removes
        // the DC the one-pole low pass leaves behind and any sub-100 Hz content, which on a small
        // VR speaker is cone excursion that produces no sound.
        HighPass(d, rate, 110f);

        Normalise(d, peak);
        return Finish(name, d, rate);
    }

    /// <summary>
    /// "Mäusepiepen". A rodent call is a fast upward FM chirp with a little vibrato, high and very
    /// short. THE ONE CLIP A RECORDING WOULD BEAT (see the class doc) — kept because 90 ms once
    /// every 26 s under a bed is not where realism is won.
    /// </summary>
    private static AudioClip MakeSqueak(int rate)
    {
        int n = (int)(rate * 0.09f);
        var d = new float[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 0.09f;
            // 3.4 -> 5.6 kHz with a 90 Hz vibrato. High, but 90 ms long: too short to mask anything
            // and far too short to be "a tone".
            float f = 3400f + 2200f * u + 140f * Mathf.Sin(2f * Mathf.PI * 90f * t);
            phase += 2f * Mathf.PI * f / rate;
            // a raised-cosine envelope: no click at either end, which is what would make it a chirp
            // rather than a squeak
            float env = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * u);
            d[i] = (Mathf.Sin(phase) * 0.85f + Mathf.Sin(phase * 2f) * 0.15f) * env;
        }

        Normalise(d, 0.85f);
        return Finish("Squeak", d, rate);
    }

    /// <summary>Small claws on stone: a run of 15 dry ticks, irregularly spaced (a real gait is
    /// not a metronome) and getting quieter as the animal goes. An even beat
    /// (<c>shrink = 1</c>) — an animal crossing a room does not accelerate.</summary>
    private static AudioClip MakeSkitter(int rate)
    {
        int n = (int)(rate * 0.55f);
        var d = new float[n];
        var r = new Rng(0x5C177E2u);

        var steps = new float[15];
        EnvSoundSchedule.SlipTrain(steps, 0.01f, 0.52f, shrink: 1f, jitter: 0.28f, seed: 0x5C177E2u);
        for (int s = 0; s < steps.Length; s++)
        {
            float t = steps[s];
            int at = (int)(t * rate);
            float amp = (1f - t / 0.55f) * (0.55f + 0.45f * Mathf.Abs(r.Next()));
            int len = (int)(rate * 0.006f);
            for (int i = 0; i < len && at + i < n; i++)
                d[at + i] += r.Next() * amp * Mathf.Exp(-i / (float)len * 5f);
        }

        HighPass(d, rate, 1400f);
        LowPass(d, rate, 7000f);
        Normalise(d, 0.7f);
        return Finish("Skitter", d, rate);
    }

    // =============================================================================================
    //  THERE IS NO ICE SOUND. ModBuild 149, and the absence is a RULING, not a gap.
    // =============================================================================================
    //
    //  USER RULING, verbatim: "Entferne das Geräusch für Eis komplett."  ("Remove the sound for ice
    //  completely.")
    //
    //  WHAT WAS HERE, so that nobody rebuilds it a third time believing it was never tried. ModBuild
    //  148 replaced a three-sine chime on a fixed 0.45 s beat with a physically reasoned burst of
    //  brittle fracture: seven cracks at power-law (Gutenberg-Richter) sizes, per-crack ring
    //  frequencies so no two shared a pitch, Q = 6 so nothing rang long enough to BE a pitch, gaps
    //  that widened as each crack relieved its own stress, and a Poisson interval in
    //  EnvSound.TickFrost so the cadence had no beat either. It was -6.7 dB in RMS against the clip
    //  the user called "super nervig", three times rarer, and reached half as far.
    //
    //  AND IT IS STILL DELETED. The verdict after that rebuild was not "quieter" or "rarer" — it was
    //  that ice should make NO sound at all, which is a judgement about the room and not about the
    //  synthesis, and no amount of better fracture physics answers it. Ice is a thing you SEE here:
    //  the frost crust is EnvRoom/EnvGrowth's and is untouched by this file. Deleting the generator
    //  rather than muting it is the point — a clip nobody plays, or a gain of zero, leaves the next
    //  reader hunting for the dial that turns it back on.
    //
    //  WHAT WENT WITH IT: EnvSoundClip.Frost, the Frost property, MakeFrost and its eight tuning
    //  constants, EnvSound._frostNode and its Find(), EnvSound.TickFrost, FrostMeanSeconds*, and the
    //  teardown lines for all of them. EnvSoundSchedule.PoissonGap SURVIVES with no caller in the
    //  mod: it carries a termination proof and its own wire vectors, and it is the shape the next
    //  statistically-scheduled event will want. See the note on it in EnvSoundSchedule.cs.

    // ---- loops ----------------------------------------------------------------------------------

    /// <summary>Earth: a slow settling under the floor. Two very low sines a few Hz apart (so they
    /// beat, and the beat is the "movement") plus low-passed noise for body.</summary>
    private static AudioClip MakeRumble(int rate)
    {
        int n = rate * 6;
        var d = new float[n];
        var r = new Rng(0xEA27Fu);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            d[i] = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.5f
                   + Mathf.Sin(2f * Mathf.PI * 47.5f * t) * 0.35f
                   + r.Next() * 0.5f;
        }

        LowPass(d, rate, 110f);
        LoopFade(d, rate / 2);
        Normalise(d, 0.8f);
        return Finish("Rumble", d, rate);
    }

    // =============================================================================================
    //  MakeChirr, DELETED — ModBuild 226. IT WAS THE "REGEN".
    // =============================================================================================
    //
    //  USER REPORT, hardware on ModBuild 225, verbatim:
    //
    //      "Im Wald gefällt mir nur dieser 'Regen' Sound nicht der ab und zu kommt und für eine Zeit
    //       bleibt, ansonsten finde ich es sehr gut."
    //
    //  WHAT THIS WAS. Band-limited noise at 2200..6500 Hz, amplitude-modulated by band-limited NOISE
    //  at 3..30 Hz — the wood's insect floor, played on the Ground node at gain 0.050 through a
    //  3000 Hz runtime corner. EnvSound.cs's THE INSECT CHORUS, DELETED carries the full verdict:
    //  the schedule that matches his sentence measured off the shipped hash (arrives every ~62 s,
    //  holds ~22 s, 25% duty cycle), the RAIN CONTROL it was measured against, and the with/without
    //  render of the whole room. The one number to keep in mind here is the band: this generator put
    //  81.0% of its energy above 2 kHz, against a rain control's 94.5% and 3.7% for the next
    //  continuous emitter in that room. It was not a texture with a hiss under it; measured as a
    //  spectrum it WAS the hiss.
    //
    //  WHY THE GENERATOR GOES RATHER THAN THE BED ALONE. Same rule as the ice sound and the two room
    //  tones before it: a clip nobody plays, or a gain of zero, leaves the next reader hunting for
    //  the dial that turns it back on. And the previous round's own written next step for this clip
    //  — "a second high-pass pole at ChirrLoHz would take the broad skirt out from under the
    //  crickets" — is now moot, because the user is not asking for a cleaner insect bed; he is
    //  asking for that emitter not to be there, and he likes everything else.
    //
    //  TWO REPAIRS DIE WITH IT AND BOTH WERE REAL. They are recorded because the DEFECT CLASSES are
    //  what the next generator has to avoid, and because "we deleted it" must not read as "we gave up
    //  on it":
    //
    //    1. THE CLIP AND ITS FILTER WERE FIGHTING. This band is 2200..6500 Hz and EnvSound.BuildSwamp
    //       created the bed WITHOUT `lowPassHz: 0` until ModBuild 221, so it also carried the default
    //       runtime AudioLowPassFilter at BedLowPassHz = 1150 Hz — one pole, -6.5 dB at 2200 and
    //       -15.3 dB at 6500. Every hertz this generator produced was being attenuated by a filter
    //       that exists to keep BODY out of the speech band, on the one clip in the bank that has no
    //       body. It played in no shipped build until 221 moved the corner to 3000 Hz. THE LESSON:
    //       AddBed's default corner is right for a bed with body and wrong for one without, and the
    //       symptom is a bed nobody can hear rather than a bed that sounds bad.
    //
    //    2. THE MODULATOR WAS A METRONOME, AND NO SPECTRAL INSTRUMENT COULD SEE IT. Until ModBuild
    //       222 it was three summed sines:
    //           m(t) = 0.55 + 0.20 sin(2*pi*17.3 t) + 0.14 sin(2*pi*23.9 t) + 0.11 sin(2*pi*31.1 t)
    //       and the doc above it claimed the three rates were "non-commensurate so the texture never
    //       settles into a pulse". They are not and it did: all three are exact multiples of 0.1 Hz,
    //       and they very nearly RE-ALIGN at a tenth of the 10 s period — analytic autocorrelation
    //       +0.970 at a lag of 0.290 s and -0.993 at 0.145 s, i.e. a hard 3.45 Hz beat with a perfect
    //       anti-phase at the half period. Off the finished buffer the replica measured 0.867 at
    //       0.291 s against 0.05-0.12 for every other bed. The user's word was "im Hintergrund ein
    //       Traktor". The 222 fix was to modulate with band-limited NOISE, whose autocorrelation
    //       decays with its own bandwidth and is therefore zero at every lag the ear could call a
    //       rhythm BY CONSTRUCTION rather than by arithmetic nobody re-checked (measured 0.123 at
    //       2.32 s, no peak anywhere in 0.05..6.0 s).
    //       THE LESSON, AND IT IS THE ONE WORTH CARRYING: the band split of the buffer was IDENTICAL
    //       before and after to a tenth of a percentage point (0.0/0.5/3.2/15.1/46.6/34.5 against
    //       0.0/0.5/3.2/15.2/46.6/34.4) and so was its centroid (3383 Hz against 3381). A round that
    //       looks for a fault like this has to measure TIME. And: ANY finite sum of sines re-aligns
    //       somewhere — choosing irrational-looking rates only moves the lag it happens at.
    //
    //  THE GENERATOR IS PRESERVED SAMPLE FOR SAMPLE in .planning/envsound-replica/room.py as
    //  make_chirr (and its pre-222 form as mb221_chirr), because it is the BEFORE column of every
    //  table in the two blocks cited above, and a deletion whose before-column has been deleted is
    //  one nobody can check.
    //
    //  IF A FUTURE ROUND WANTS CRICKETS IN THE WOOD, IT MUST NOT REBUILD THIS. What the user calls
    //  rain is broadband noise; a cricket is a NARROWBAND TONAL CHIRP with a pulse rate. That is a
    //  different generator, and it should arrive as EVENTS on the shared clock the way the owl and
    //  the night bird do — which is the form of this feature he has repeatedly said he likes.

    // =============================================================================================
    //  THE NIGHT CALLS — ModBuild 222. The first sounds in this bank that are ANIMALS in the wood.
    // =============================================================================================
    //
    //  USER REQUEST, 2026-08-22, verbatim, and this is the whole specification:
    //
    //      "Dezenter Wind kann bleiben und ansonsten eventuell hier und da noch ein ruf von tieren
    //       (was man so im Wald in der Nacht hört)"
    //
    //  TWO CALLS, AT OPPOSITE ENDS OF THE BAND, so the wood does not repeat itself: a tawny owl near
    //  by (fluty, ~395 Hz, 2.3 s) and a small bird further off (three thin whistles at ~2.8-3.1 kHz,
    //  0.78 s). They are SCHEDULED, not baked — EnvSound.TickNightCall — because an event that
    //  recurs with a buffer is item 6 of the class doc's whole complaint, and because a call has to
    //  come from a PLACE ("verortbar von seinen entsprechenden Quellen"): the owl is put on the
    //  canopy and the bird on the far trunks.
    //
    //  THE OWL IS 95.8% INSIDE 200-500 Hz — THE BAND THIS ROUND JUST EVICTED THE WOOD'S BED FROM —
    //  AND THAT IS NOT A CONTRADICTION. It is the distinction the whole round rests on. The tractor
    //  was a broadband WASH with no onset, no end, no direction and no change; this is a near-SINE
    //  (H2 at 0.20, H3 at 0.06 and nothing else) with an attack, a downward glide, a tremolo and a
    //  stop, arriving about once a minute from a tree. An owl that was not in that band would not be
    //  an owl — a tawny owl's fundamental really is 350-500 Hz — and moving it up to be safe would
    //  answer the report by deleting the thing he asked for.
    //
    //  REJECTED:
    //    * A FROG. It was the third candidate and it is the one that would have been dangerous: a
    //      croak is a PULSED low-mid buzz at 30-45 Hz, which is a low-mid carrier with a hard
    //      periodic envelope — the exact shape that had just been removed from the chirr. A wood at
    //      night can have one; this bank should not add one in the round that fixed a beat.
    //    * A CC0 RECORDING, for the class doc's reasons and for one specific to a call: a recorded
    //      bird brings a recorded WOOD with it (its own reverb, its own distance, its own weather),
    //      which would then be heard inside ours. A synthesized call has the room it is played in.
    //    * PLAYING THEM THROUGH THE CANOPY'S OWN BED SOURCE, the way the fire's crackle goes through
    //      the roar's (AudioSource.PlayOneShot). The crackle does that to inherit the fire's GATE;
    //      these calls have no gate to inherit, the canopy bed is Air-gated and would silence them
    //      whenever the wind is down, and the one-shot pool already exists and is idle in this room —
    //      the wood schedules NO other one-shot at all.

    /// <summary>
    /// THE OWL'S PHRASES. A tawny owl (Waldkauz) calls in a shape everyone in central Europe knows:
    /// a long held note, a pause, a very short one, a pause, and a longer tremulous one. Each row is
    /// (start, length, level, tremolo).
    ///
    /// <para>THE PAUSES ARE COMPRESSED. A real bird leaves two to four seconds between the first
    /// phrase and the last; at that length the clip would be six seconds of mostly silence occupying
    /// a one-shot voice, and the gaps would be long enough for the player to hear them as three
    /// separate events. 0.42 s and 0.23 s keep the phrasing recognisable inside 2.3 s.</para></summary>
    private static readonly float[][] OwlPhrases =
    {
        new[] { 0.00f, 0.52f, 1.00f, 0f },
        new[] { 0.94f, 0.13f, 0.55f, 0f },
        new[] { 1.28f, 0.92f, 0.90f, 1f },
    };

    /// <summary>The owl's fundamental and the fall across a phrase. 395 Hz is a male tawny owl's
    /// hoot; the 0.93 ratio is a fall of about an eighth of an octave over the phrase, which is what
    /// makes it a call rather than a held tone.
    ///
    /// <para>H2 AT 0.20 AND H3 AT 0.06 — a FLUTE, deliberately. An owl's hoot is one of the nearest
    /// things in nature to a pure sine, and that is also what keeps it clear of this round's fault:
    /// a broadband source at 395 Hz would be an engine, and a sine at 395 Hz cannot be.</para>
    ///
    /// <para>THE BREATH is 300..1200 Hz noise at 0.055 of the tone. Without it the clip is a
    /// synthesizer patch — MakeDrips' scar exactly, where a pure glided sine read as "a bell". A bird
    /// moves air to make a sound, and a trace of that air is what stops the ear filing it as
    /// electronic.</para></summary>
    private const float OwlF0 = 395f;
    private const float OwlFall = 0.93f;
    private const float OwlH2 = 0.20f;
    private const float OwlH3 = 0.06f;
    private const float OwlBreath = 0.055f;
    private const float OwlBreathLoHz = 300f;
    private const float OwlBreathHiHz = 1200f;

    /// <summary>The tremolo on the last phrase, and where in it the tremolo starts. 13.7 Hz is inside
    /// the range THE CANDLE's <c>FlutterHz</c> names as flutter rather than roughness, and it lasts
    /// under half a second — this is a bird's throat, not a modulator on a bed, and the distinction
    /// this round exists to enforce is between a beat that never stops and one that is part of a
    /// 2.3 s event.</summary>
    private const float OwlTremHz = 13.7f;
    private const float OwlTremDepth = 0.34f;
    private const float OwlTremFrom = 0.42f;

    private const float OwlSeconds = 2.30f;
    private const float OwlPeak = 0.80f;
    private const uint OwlSeed = 0x71B4C000u;

    /// <summary>
    /// AN OWL, near by. Three fluty phrases with a breath in them and a tremolo on the last.
    /// </summary>
    private static AudioClip MakeOwl(int rate)
    {
        int n = (int)(rate * OwlSeconds);
        var d = new float[n];

        // ---- the breath, once, for the whole clip: a band of noise the phrases dip into.
        var nb = new float[n];
        var nr = new Rng(OwlSeed);
        for (int i = 0; i < n; i++)
            nb[i] = nr.Next();
        HighPass(nb, rate, OwlBreathLoHz);
        LowPass(nb, rate, OwlBreathHiHz);
        Normalise(nb, 1f);

        for (int p = 0; p < OwlPhrases.Length; p++)
        {
            int at = (int)(OwlPhrases[p][0] * rate);
            int len = (int)(OwlPhrases[p][1] * rate);
            float level = OwlPhrases[p][2];
            bool trem = OwlPhrases[p][3] > 0.5f;
            if (len <= 0)
                continue;

            float phase = 0f;
            for (int i = 0; i < len && at + i < n; i++)
            {
                float u = i / (float)len;
                // THE GLIDE. The frequency is integrated into a phase rather than written as
                // sin(2*pi*f(t)*t), which is the standing trap in this file: the second form sweeps
                // at twice the intended rate and lands on the wrong note.
                float f = OwlF0 * (1f + (OwlFall - 1f) * u);
                phase += 2f * Mathf.PI * f / rate;

                // A soft open and a softer close, smoothstepped at both shoulders so neither end
                // clicks — a click is what would make this a chirp instead of a hoot.
                float env = Mathf.Min(1f, u / 0.11f) * Mathf.Min(1f, (1f - u) / 0.24f);
                env = env * env * (3f - 2f * env);
                if (trem)
                {
                    float g = Mathf.Max(0f, (u - OwlTremFrom) / (1f - OwlTremFrom));
                    env *= 1f - OwlTremDepth * g * 0.5f
                                * (1f - Mathf.Cos(2f * Mathf.PI * OwlTremHz * (i / (float)rate)));
                }

                float tone = Mathf.Sin(phase)
                             + OwlH2 * Mathf.Sin(2f * phase)
                             + OwlH3 * Mathf.Sin(3f * phase);
                d[at + i] += level * env * (tone + OwlBreath * nb[at + i]);
            }
        }

        Normalise(d, OwlPeak);
        return Finish("Owl", d, rate);
    }

    /// <summary>THE NIGHT BIRD'S THREE NOTES — (start, length, level). Falling in level, which is
    /// what a contact call does; a run of three equal notes reads as a machine and a run of three
    /// rising ones reads as an alarm, and neither is "in der Nacht".</summary>
    private static readonly float[][] BirdNotes =
    {
        new[] { 0.000f, 0.085f, 1.00f },
        new[] { 0.235f, 0.080f, 0.86f },
        new[] { 0.470f, 0.090f, 0.72f },
    };

    /// <summary>The bird's first note, the rise across one note, the step between notes and the
    /// second harmonic. 2760 Hz with a 13% rise is a thin upward whistle; 3.5% of step per note keeps
    /// the three from being one pitch repeated. H2 at 0.13 and nothing above it — a small bird's
    /// whistle is very nearly a sine, and the 0.78 s the whole call lasts is far too short for the
    /// 1-4 kHz it occupies to mask anything.</summary>
    private const float BirdF0 = 2760f;
    private const float BirdRise = 1.13f;
    private const float BirdStep = 0.035f;
    private const float BirdH2 = 0.13f;

    private const float BirdSeconds = 0.78f;
    private const float BirdPeak = 0.78f;

    /// <summary>
    /// A SMALL BIRD, further off. Three thin whistles, each a raised cosine so nothing clicks.
    /// </summary>
    private static AudioClip MakeNightBird(int rate)
    {
        int n = (int)(rate * BirdSeconds);
        var d = new float[n];

        for (int k = 0; k < BirdNotes.Length; k++)
        {
            int at = (int)(BirdNotes[k][0] * rate);
            int len = (int)(BirdNotes[k][1] * rate);
            float level = BirdNotes[k][2];
            if (len <= 0)
                continue;

            float phase = 0f;
            float f0 = BirdF0 * (1f + BirdStep * k);
            for (int i = 0; i < len && at + i < n; i++)
            {
                float u = i / (float)len;
                phase += 2f * Mathf.PI * (f0 * (1f + (BirdRise - 1f) * u)) / rate;
                float env = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * u);
                d[at + i] += level * env * (Mathf.Sin(phase) + BirdH2 * Mathf.Sin(2f * phase));
            }
        }

        Normalise(d, BirdPeak);
        return Finish("NightBird", d, rate);
    }

    // =============================================================================================
    //  THE WOOD'S VOCABULARY — ModBuild 241. FIVE MORE ANIMALS AND NOT ONE MORE EVENT PER MINUTE.
    // =============================================================================================
    //
    //  USER REQUEST, 2026-08-24, verbatim, and the bracket is the acceptance criterion rather than
    //  an aside:
    //
    //      "Füge noch mehr verschiedene Tiersounds hinzu die zu einem Wald in der Nacht passen für
    //       mehr Varianz (nicht mehr Häufigkeit)."
    //
    //  THE RATE IS HELD, AND HERE IS THE MECHANISM THAT HOLDS IT, NAMED SO THE NEXT ROUND DOES NOT
    //  "IMPROVE" THE VARIETY BY TURNING IT UP. The wood sounds AT MOST ONE CALL PER 41 s SLOT and
    //  78% of slots carry one, i.e. a call about every 53 s — `EnvSound.NightCallSlot`,
    //  `NightCallMean` and `NightCallSkip`, all three byte-for-byte what ModBuild 223 shipped and
    //  what 226 deliberately declined to re-tune ("er hat 'ansonsten finde ich es sehr gut' über den
    //  Build geschrieben, in dem diese Zahlen stecken"). NOTHING in this round touches them. What
    //  changed is one line in `EnvSound.TickNightCall`: where it used to toss a weighted coin
    //  between two clips, it now DEALS A CARD from a seven-card vocabulary —
    //  `EnvSoundSchedule.DeckDraw`. Seven clips on the same schedule is seven times the vocabulary
    //  at exactly the same number of events; seven clips on seven schedules would have been seven
    //  times the events, which is the sentence in brackets.
    //
    //  THE SEVEN, AND WHY EACH IS DISTINGUISHABLE FROM THE OTHER SIX. Every number below is measured
    //  off the finished buffer, by .planning/envsound-replica's own instruments — `audible()` for
    //  the centroid and spread over 200 Hz-12 kHz (the band a Quest 3 returns) and `bands()` for the
    //  split; the attack is the 10-90% rise of a 5 ms RMS envelope. The generators driven were a
    //  line-for-line transcription of the five below, INCLUDING the HarmonicStack recurrence, and it
    //  agrees with the tuning prototype to 9e-14 — so these are this file's numbers and not a
    //  neighbouring implementation's.
    //
    //  THE REPLICA DOES NOT YET CARRY THESE FIVE GENERATORS, and it should: room.py holds make_owl
    //  and make_bird for exactly this reason, and a table whose instrument has been thrown away is a
    //  claim rather than a measurement. That file is outside this round's lane; the patch adding
    //  make_kewick / make_fox / make_raven / make_deer / make_owlet beside them went to the
    //  integrator with this change.
    //
    //                  dur     centroid  spread   attack   the rhythm, which is the other half of it
    //    Owl          2.30 s    394 Hz   0.22 oct  37 ms   3 fluty phrases, tremolo on the last
    //    RoeDeer      0.34 s    518 Hz   1.18 oct   5 ms   ONE cough. Nothing else here is one event
    //    Raven        1.14 s   1150 Hz   0.71 oct  18 ms   2 dry rasps 0.72 s apart
    //    Fox          1.06 s   1390 Hz   1.21 oct   9 ms   3 hoarse barks, 0.40 and 0.47 s apart
    //    KeWick       0.46 s   1926 Hz   0.69 oct   —      2 syllables 0.135 s apart, the 2nd higher
    //    NightBird    0.78 s   3055 Hz   0.14 oct  26 ms   3 thin whistles 0.235 s apart
    //    OwletBeg     1.86 s   3532 Hz   0.48 oct  54 ms   2 long rasps 1.20 s apart
    //
    //  The centroid ladder is 394 / 518 / 1150 / 1390 / 1926 / 3055 / 3532 Hz. The two closest pairs
    //  are Raven-Fox (0.27 oct) and NightBird-OwletBeg (0.21 oct), and BOTH are separated on the
    //  other three columns instead: the fox is 1.21 oct of spread against the raven's 0.71 and has
    //  three bursts against two, and the owlet is NOISE (0.48 oct) where the night bird is a
    //  near-pure whistle (0.14 oct) and lasts 1.86 s against 0.78. A ladder that separated only on
    //  centroid would be seven notes; these are seven animals.
    //
    //  EVERY ONE OF THEM HAS A THROAT, which is MakeOwl's rule and MakeDrips' scar — "a pure glided
    //  sine reads as a bell, not a bird". No call below is a sine with an envelope on it: the
    //  ke-wick is a five-harmonic reed with breath noise, the fox and the roe deer are harmonic
    //  stacks whose FUNDAMENTAL IS JITTERED by band-limited noise (that is hoarseness — irregular
    //  vocal folds — and it is the single thing that stops a bark sounding like a buzzer), the raven
    //  is a formant-shaped stack with a PERIOD-DOUBLING sub-oscillation at f0/2 (corvid calls really
    //  do this, and it is where the rasp comes from), and the owlet is more noise than tone.
    //
    //  REJECTED, and the first is the one a later round will want to re-propose:
    //
    //    * A NIGHTJAR'S CHURR. It is the best "different rhythm, different duration" candidate in
    //      central Europe — a long mechanical trill unlike anything else here — and it is exactly
    //      the shape THE NIGHT CALLS rejected a frog for: "a PULSED low-mid buzz at 30-45 Hz, which
    //      is a low-mid carrier with a hard periodic envelope — the exact shape that had just been
    //      removed from the chirr". A nightjar's churr is a low-mid carrier pulsed at about 30 Hz.
    //      That ruling is two rounds old, it was written about a beat the user reported twice in his
    //      own words ("im Hintergrund ein Traktor", "super nervig"), and a round about VARIETY is
    //      not the round to re-litigate it with no hardware in the loop.
    //    * CRICKETS, in any form. Refused at ModBuild 226 with a user sentence attached; see
    //      MakeChirr, DELETED above. Not re-opened, not re-argued, not partially re-introduced as
    //      "an event".
    //    * A VIXEN'S SCREAM rather than a fox's bark. It is the more famous sound and it is a
    //      genuine one, but it is a SCREAM — the whole point of it is that it is startling — against
    //      a feature whose standing rule is "nie aufdringlich" and a bank whose one design law is
    //      that the frightening sound is never the loud one. The bark keeps the fox and loses the
    //      jump.
    //    * A WOOD PIGEON'S PHRASE. Rejected on band, not on taste: a five-note coo sits at 480-600
    //      Hz with a near-sine timbre, which is the owl's cell in the table above. Adding a card
    //      that is hard to tell from a card already in the deck adds frequency, not variety.
    //    * RECORDINGS, for THE NIGHT CALLS' reason, which has not changed: a recorded bird brings a
    //      recorded WOOD with it — its own reverb, its own distance, its own weather — which is then
    //      heard inside ours. A synthesized call has the room it is played in.
    //
    //  THE COST, stated rather than left to be discovered. 233,279 samples at 48 kHz = 4.86 s of
    //  new mono PCM = 933,116 bytes = 0.89 MB of float32, against a bank that was about 2 MB, and
    //  every byte of it is resident for the session. The synthesis is one pass per clip on the frame
    //  the room is first placed; the harmonic stacks are the only new work of any size and they use
    //  the Chebyshev recurrence below rather than one Mathf.Sin per harmonic per sample, which takes
    //  the added trig from ~1.2 M calls to ~330 k. The remaining arithmetic is 233 k noise draws,
    //  eight one-pole filter passes over them and ~750 k formant divides. That should be on the
    //  order of 20-30 ms on top of the bank's measured ~112 ms — an OPERATION COUNT, not a
    //  measurement: this file cannot be built outside Unity, so the real number is whatever
    //  Build()'s own log line says on the next hardware run, and that line is the place to check it.

    /// <summary>
    /// SIN(k*phase) FOR A WHOLE HARMONIC STACK FROM ONE SIN AND ONE COS — the Chebyshev recurrence
    /// <c>sin(k p) = 2 cos(p) sin((k-1) p) - sin((k-2) p)</c>, seeded with <c>sin(0 p) = 0</c> and
    /// <c>sin(1 p) = sin p</c>.
    ///
    /// <para>WHY IT IS HERE AT ALL. Three of the five calls below sum 12-18 harmonics per sample.
    /// Written the obvious way that is 1.2 million <c>Mathf.Sin</c> calls added to a bank build that
    /// already takes ~112 ms and runs on the main thread on the frame the room is placed — see
    /// <see cref="Build"/>'s log line for why that frame is watched. The recurrence replaces every
    /// harmonic after the first with one multiply and one subtract. It is EXACT in exact arithmetic
    /// and holds to 9.4e-15 in double over k = 1..18; in float, over the same range, the drift is
    /// far below anything a spectrum shows.</para>
    ///
    /// <para>Caller-driven rather than a loop of its own so the weights can be anything — a fixed
    /// tilt, a formant that slides as the fundamental falls — without this having to know.</para>
    /// </summary>
    private struct HarmonicStack
    {
        private float _cos2;   // 2 cos(p), the recurrence's only coefficient
        private float _prev;   // sin((k-1) p)
        private float _cur;    // sin(k p)

        /// <summary>Start a stack at <paramref name="phase"/>. After this, <see cref="Current"/> is
        /// the FUNDAMENTAL — the first harmonic is not a step, it is the seed.</summary>
        internal HarmonicStack(float phase)
        {
            _cos2 = 2f * Mathf.Cos(phase);
            _prev = 0f;
            _cur = Mathf.Sin(phase);
        }

        /// <summary>sin(k*phase) for the harmonic the stack is standing on.</summary>
        internal float Current => _cur;

        /// <summary>Step to the next harmonic and return it.</summary>
        internal float Next()
        {
            float next = _cos2 * _cur - _prev;
            _prev = _cur;
            _cur = next;
            return next;
        }
    }

    /// <summary>A band of noise, normalised to unity — the "throat" every call below dips into.
    /// One-pole either side, which is the same pair of filters <see cref="MakeOwl"/> builds its
    /// breath from; the skirts are gentle and that is wanted, because a brick-walled band of noise
    /// reads as a filter sweep rather than as air.</summary>
    private static float[] NoiseBand(int n, int rate, uint seed, float loHz, float hiHz)
    {
        var b = new float[n];
        var r = new Rng(seed);
        for (int i = 0; i < n; i++)
            b[i] = r.Next();
        HighPass(b, rate, loHz);
        LowPass(b, rate, hiHz);
        Normalise(b, 1f);
        return b;
    }

    /// <summary>A smoothstepped open and close on a note, as a fraction of its own length. Both
    /// shoulders, because a click at either end is what turns a call into a chirp —
    /// <see cref="MakeOwl"/> makes the argument in full.</summary>
    private static float Shoulders(float u, float attack, float release)
    {
        float e = Mathf.Min(1f, u / attack) * Mathf.Min(1f, (1f - u) / release);
        return e * e * (3f - 2f * e);
    }

    // ---- 1. THE KE-WICK ---------------------------------------------------------------------

    /// <summary>THE TWO SYLLABLES — (start, length, level, f0, bend, skew). A tawny owl's female
    /// answers the male's hoot with a short "ke" and a longer, higher, sharply inflected "wick", and
    /// the pair is what a central-European wood at night actually sounds like to most people: the
    /// hoot is the famous one, the ke-wick is the common one.
    ///
    /// <para>BEND AND SKEW ARE THE INFLECTION. A "wick" does not glide one way like the hoot does —
    /// it rises hard, tops out, and falls away, so the frequency follows an ARC
    /// <c>sin(pi * u^skew)</c> rather than a line. A skew below 1 puts the top of the arc EARLY in
    /// the syllable, which is what makes it sound flicked rather than swelled; 0.55 puts it at
    /// u = 0.32.</para></summary>
    private static readonly float[][] KeWickSyllables =
    {
        new[] { 0.000f, 0.075f, 0.55f,  880f, 0.10f, 0.80f },
        new[] { 0.135f, 0.235f, 1.00f, 1150f, 0.34f, 0.55f },
    };

    /// <summary>The reed, harmonic by harmonic. The hoot is a FLUTE (H2 0.20, H3 0.06 and nothing
    /// else); the ke-wick is the same bird's other voice and it is not fluty at all — it is sharp,
    /// slightly harsh, and reads that way because it carries five harmonics with a shallow tilt.
    /// This is also what puts it at 1926 Hz with a 880-1150 Hz fundamental: what the ear hears in a
    /// ke-wick is the STACK, not the note.</summary>
    private static readonly float[] KeWickHarmonics = { 1.00f, 0.55f, 0.38f, 0.22f, 0.12f };

    /// <summary>The breath, as a fraction of the tone, and the band it occupies. 0.16 against the
    /// hoot's 0.055: a ke-wick is a much breathier call than a hoot, and the band sits over the
    /// reed rather than under it (the hoot's breath is 300-1200 Hz, below its own stack).</summary>
    private const float KeWickBreath = 0.16f;
    private const float KeWickBreathLoHz = 1000f;
    private const float KeWickBreathHiHz = 4500f;

    private const float KeWickSeconds = 0.46f;
    private const float KeWickPeak = 0.80f;
    private const uint KeWickSeed = 0x2C51D000u;

    /// <summary>THE FEMALE TAWNY OWL'S CONTACT CALL. Two syllables, the second higher and inflected.</summary>
    private static AudioClip MakeKeWick(int rate)
    {
        int n = (int)(rate * KeWickSeconds);
        var d = new float[n];
        float[] nb = NoiseBand(n, rate, KeWickSeed, KeWickBreathLoHz, KeWickBreathHiHz);

        for (int p = 0; p < KeWickSyllables.Length; p++)
        {
            int at = (int)(KeWickSyllables[p][0] * rate);
            int len = (int)(KeWickSyllables[p][1] * rate);
            float level = KeWickSyllables[p][2];
            float f0 = KeWickSyllables[p][3];
            float bend = KeWickSyllables[p][4];
            float skew = KeWickSyllables[p][5];
            if (len <= 0)
                continue;

            float phase = 0f;
            for (int i = 0; i < len && at + i < n; i++)
            {
                float u = i / (float)len;
                // THE ARC, integrated into a phase — MakeOwl's standing trap, restated because it
                // is the one mistake this whole family of generators keeps offering: writing
                // sin(2*pi*f(t)*t) sweeps at twice the intended rate and lands on the wrong note.
                float f = f0 * (1f + bend * Mathf.Sin(Mathf.PI * Mathf.Pow(u, skew)));
                phase += 2f * Mathf.PI * f / rate;

                float env = Shoulders(u, 0.09f, 0.45f);
                var stack = new HarmonicStack(phase);
                float tone = KeWickHarmonics[0] * stack.Current;
                for (int k = 1; k < KeWickHarmonics.Length; k++)
                    tone += KeWickHarmonics[k] * stack.Next();

                d[at + i] += level * env * (tone + KeWickBreath * nb[at + i]);
            }
        }

        Normalise(d, KeWickPeak);
        return Finish("KeWick", d, rate);
    }

    // ---- 2. THE FOX -------------------------------------------------------------------------

    /// <summary>THE THREE BARKS — (start, length, level). A red fox barks in short SERIES, and the
    /// gaps are 0.400 and 0.470 s rather than one number twice: three evenly spaced bursts inside
    /// one second is a 2.4 Hz beat, and this file has deleted a whole cue for less. Falling in
    /// level, because a series does.</summary>
    private static readonly float[][] FoxBarks =
    {
        new[] { 0.000f, 0.115f, 1.00f },
        new[] { 0.400f, 0.105f, 0.88f },
        new[] { 0.870f, 0.100f, 0.72f },
    };

    /// <summary>The fox's fundamental, the fall across one bark, and the harmonic stack.
    /// 450 Hz falling to 324 (0.72) is a real fox bark's pitch contour — a bark DROPS, and a bark
    /// that does not is a beep. Sixteen harmonics on a shallow 1/k^0.56 tilt is a near-sawtooth
    /// larynx: broadband on purpose, which is what makes this the widest-spread clip in the bank
    /// (1.21 octaves) and what a fox actually is.</summary>
    private const float FoxF0 = 450f;
    private const float FoxFall = 0.72f;
    private const int FoxHarmonics = 16;
    private const float FoxTilt = 0.56f;

    /// <summary>THE HOARSENESS, and it is the constant that decides whether this is an animal.
    /// A fox's vocal folds do not close cleanly, so the fundamental WANDERS by a few per cent from
    /// cycle to cycle. 7.5% of noise low-passed at 130 Hz is that wander. Take it out and the same
    /// harmonic stack reads as a car horn: a stack with a perfectly steady f0 is what a synthesizer
    /// makes and what a throat cannot.</summary>
    private const float FoxJitter = 0.075f;
    private const float FoxJitterHz = 130f;

    /// <summary>The throat the barks are shaped by, and the noise mixed into them. The band is
    /// applied TWICE: as the band of the noise, and (after every bark is summed) as a pair of poles
    /// over the whole buffer, which is what a mouth does to its own output. The low corner on the
    /// buffer is deliberately 0.55 of the noise's, so the fundamental is thinned rather than
    /// removed — you hear a fox's bark through its harmonics, not its pitch.</summary>
    private const float FoxThroatLoHz = 900f;
    private const float FoxThroatHiHz = 3600f;
    private const float FoxThroatLoTilt = 0.55f;
    private const float FoxNoise = 0.40f;

    private const float FoxSeconds = 1.06f;
    private const float FoxPeak = 0.76f;
    private const uint FoxSeed = 0x6B0FA000u;
    private const uint FoxJitterSeed = 0x6B0FB000u;

    /// <summary>A RED FOX BARKING, on the ground between the trunks. Three hoarse bursts.</summary>
    private static AudioClip MakeFox(int rate)
    {
        int n = (int)(rate * FoxSeconds);
        var d = new float[n];
        float[] nb = NoiseBand(n, rate, FoxSeed, FoxThroatLoHz, FoxThroatHiHz);

        // The pitch wander, once for the whole clip, so all three barks are the same animal.
        var jit = new float[n];
        var jr = new Rng(FoxJitterSeed);
        for (int i = 0; i < n; i++)
            jit[i] = jr.Next();
        LowPass(jit, rate, FoxJitterHz);
        Normalise(jit, 1f);

        // The tilt is fixed, so its weights are computed ONCE rather than per sample — and
        // normalised by their own sum, so FoxNoise means a real fraction of the tone rather than a
        // fraction of however many harmonics happen to be summed.
        var weight = new float[FoxHarmonics];
        float wsum = 0f;
        for (int k = 1; k <= FoxHarmonics; k++)
        {
            weight[k - 1] = 1f / Mathf.Pow(k, FoxTilt);
            wsum += weight[k - 1];
        }
        for (int k = 0; k < FoxHarmonics; k++)
            weight[k] /= wsum;

        for (int p = 0; p < FoxBarks.Length; p++)
        {
            int at = (int)(FoxBarks[p][0] * rate);
            int len = (int)(FoxBarks[p][1] * rate);
            float level = FoxBarks[p][2];
            if (len <= 0)
                continue;

            float phase = 0f;
            for (int i = 0; i < len && at + i < n; i++)
            {
                float u = i / (float)len;
                float f = FoxF0 * (1f + (FoxFall - 1f) * u) * (1f + FoxJitter * jit[at + i]);
                phase += 2f * Mathf.PI * f / rate;

                float env = Shoulders(u, 0.06f, 0.55f);
                var stack = new HarmonicStack(phase);
                float tone = weight[0] * stack.Current;
                for (int k = 1; k < FoxHarmonics; k++)
                    tone += weight[k] * stack.Next();

                d[at + i] += level * env * (tone + FoxNoise * nb[at + i]);
            }
        }

        HighPass(d, rate, FoxThroatLoHz * FoxThroatLoTilt);
        LowPass(d, rate, FoxThroatHiHz);
        Normalise(d, FoxPeak);
        return Finish("Fox", d, rate);
    }

    // ---- 3. THE RAVEN -----------------------------------------------------------------------

    /// <summary>THE TWO RASPS — (start, length, level). A corvid disturbed on its roost does not
    /// call once and it does not call ten times; it rasps, waits, and rasps again a little quieter.
    /// 0.72 s apart, which is far outside the 0.2 s the ear starts hearing as a rhythm.</summary>
    private static readonly float[][] RavenCalls =
    {
        new[] { 0.000f, 0.340f, 1.00f },
        new[] { 0.720f, 0.310f, 0.72f },
    };

    /// <summary>The raven's fundamental and its fall. 285 Hz is a corvid's voice; the 0.88 fall is
    /// gentler than the fox's because a "kraa" is HELD, not spat.</summary>
    private const float RavenF0 = 285f;
    private const float RavenFall = 0.88f;
    private const int RavenHarmonics = 18;

    /// <summary>THE FORMANT — a fixed resonance in the bird's throat at 1150 Hz with a 620 Hz
    /// half-width, applied to each harmonic by where IT lands rather than by its index. That
    /// distinction is the whole timbre: because the formant is fixed in hertz and the fundamental
    /// falls, the harmonics SLIDE THROUGH it over the length of the rasp, which is what a throat
    /// does and what a fixed 1/k tilt cannot imitate. It is also what makes this dry: 64.6% of the
    /// energy lands in 1-2 kHz and almost none below 500 Hz, so it is a rasp rather than a
    /// growl.</summary>
    private const float RavenFormantHz = 1150f;
    private const float RavenFormantBw = 620f;

    /// <summary>THE PERIOD DOUBLING, and this is where the rasp comes from. Corvid calls are full of
    /// nonlinear phenomena; the commonest is a sub-oscillation at HALF the fundamental, which fills
    /// in half-integer harmonics and reads to the ear as roughness rather than as a lower note.
    /// Implemented as an amplitude modulation at f0/2 — <c>cos(phase/2)</c>, i.e. locked to the
    /// carrier's own phase rather than to the clock, so it cannot drift into a beat. 142 Hz is a
    /// PITCH, not a rhythm: it is nowhere near the 30-45 Hz pulsing THE NIGHT CALLS rejected a frog
    /// for.</summary>
    private const float RavenSub = 0.38f;

    /// <summary>The breath in the rasp. Modest — 0.14 — because the raven's harshness is already in
    /// the sub-oscillation; noise on top of that would take it from a bird towards a hiss.</summary>
    private const float RavenNoise = 0.14f;
    private const float RavenNoiseLoHz = 800f;
    private const float RavenNoiseHiHz = 3000f;

    private const float RavenSeconds = 1.14f;
    private const float RavenPeak = 0.74f;
    private const uint RavenSeed = 0x3D96C000u;

    /// <summary>A CORVID ON ITS ROOST. Two dry rasps with a period-doubled buzz in them.</summary>
    private static AudioClip MakeRaven(int rate)
    {
        int n = (int)(rate * RavenSeconds);
        var d = new float[n];
        float[] nb = NoiseBand(n, rate, RavenSeed, RavenNoiseLoHz, RavenNoiseHiHz);

        for (int p = 0; p < RavenCalls.Length; p++)
        {
            int at = (int)(RavenCalls[p][0] * rate);
            int len = (int)(RavenCalls[p][1] * rate);
            float level = RavenCalls[p][2];
            if (len <= 0)
                continue;

            float phase = 0f;
            for (int i = 0; i < len && at + i < n; i++)
            {
                float u = i / (float)len;
                float f = RavenF0 * (1f + (RavenFall - 1f) * u);
                phase += 2f * Mathf.PI * f / rate;

                float env = Shoulders(u, 0.07f, 0.30f);

                // THE STACK THROUGH THE FORMANT. The weights are recomputed per sample on purpose —
                // see RavenFormantHz. Normalised by their own sum so the shape decides the timbre
                // and not the level.
                var stack = new HarmonicStack(phase);
                float tone = 0f;
                float wsum = 0f;
                for (int k = 1; k <= RavenHarmonics; k++)
                {
                    float value = k == 1 ? stack.Current : stack.Next();
                    float dev = (k * f - RavenFormantHz) / RavenFormantBw;
                    float w = 1f / (1f + dev * dev);
                    tone += w * value;
                    wsum += w;
                }
                tone = (tone / Mathf.Max(wsum, 1e-6f))
                       * (1f - RavenSub * 0.5f * (1f - Mathf.Cos(0.5f * phase)));

                d[at + i] += level * env * (tone + RavenNoise * nb[at + i]);
            }
        }

        Normalise(d, RavenPeak);
        return Finish("Raven", d, rate);
    }

    // ---- 4. THE ROE DEER --------------------------------------------------------------------

    /// <summary>ONE BARK, and the one is the design. A roe deer that has seen something coughs once
    /// — sometimes twice, minutes apart — and the shortness is the whole character: at 0.34 s this
    /// is by a factor of two the briefest thing the wood says, and it is the only card in the deck
    /// that is a SINGLE event rather than a phrase. A second bark inside the clip would have made it
    /// a rhythm and taken that away.</summary>
    private const float RoeDeerSeconds = 0.34f;
    private const float RoeDeerLength = 0.260f;

    /// <summary>The deer's fundamental and its fall. 235 Hz down to 160 (0.68) in a quarter of a
    /// second is a very hard drop — harder than the fox's — which is what makes it read as a cough
    /// rather than a call.</summary>
    private const float RoeDeerF0 = 235f;
    private const float RoeDeerFall = 0.68f;
    private const int RoeDeerHarmonics = 14;
    private const float RoeDeerTilt = 0.72f;

    /// <summary>The throat resonance, low and broad — 700 Hz over a 700 Hz half-width. Wider than
    /// the raven's because this is a barrel of a chest with a short mouth on it rather than a
    /// bird's syrinx, and the audible result is that the energy is SPREAD (1.18 oct) instead of
    /// banded.</summary>
    private const float RoeDeerFormantHz = 700f;
    private const float RoeDeerFormantBw = 700f;

    /// <summary>Hoarseness, as for the fox but harder — 10% at 190 Hz. A roe deer's bark is not a
    /// clean note at any point in its length.</summary>
    private const float RoeDeerJitter = 0.10f;
    private const float RoeDeerJitterHz = 190f;

    /// <summary>The breath, and it is a THIRD of this sound rather than a trace: 0.34 of the tone
    /// over 300-1800 Hz. What separates a deer's bark from a dog's is how much of it is air.</summary>
    private const float RoeDeerNoise = 0.34f;
    private const float RoeDeerNoiseLoHz = 300f;
    private const float RoeDeerNoiseHiHz = 1800f;

    /// <summary>THE ENVELOPE, and it is the only percussive one in this family. A 1.8% linear
    /// attack over a 0.26 s burst is 4.7 ms, which is what puts the measured 10-90% rise at 5.3 ms
    /// against the owl's 37 — this is a sound with a FRONT. The decay is exponential at 6.0 per
    /// length (a chest emptying, not a note ending) and a 10% release shoulder takes the last
    /// fraction of a per cent to zero, so the buffer cannot end on a step.</summary>
    private const float RoeDeerAttack = 0.018f;
    private const float RoeDeerDecay = 6.0f;
    private const float RoeDeerRelease = 0.10f;

    private const float RoeDeerPeak = 0.78f;
    private const uint RoeDeerSeed = 0x51A27000u;
    private const uint RoeDeerJitterSeed = 0x51A26000u;

    /// <summary>A ROE DEER'S ALARM BARK. One short percussive cough, from the ground, far off.</summary>
    private static AudioClip MakeRoeDeer(int rate)
    {
        int n = (int)(rate * RoeDeerSeconds);
        var d = new float[n];
        float[] nb = NoiseBand(n, rate, RoeDeerSeed, RoeDeerNoiseLoHz, RoeDeerNoiseHiHz);

        var jit = new float[n];
        var jr = new Rng(RoeDeerJitterSeed);
        for (int i = 0; i < n; i++)
            jit[i] = jr.Next();
        LowPass(jit, rate, RoeDeerJitterHz);
        Normalise(jit, 1f);

        var tilt = new float[RoeDeerHarmonics];
        for (int k = 1; k <= RoeDeerHarmonics; k++)
            tilt[k - 1] = 1f / Mathf.Pow(k, RoeDeerTilt);

        int len = (int)(RoeDeerLength * rate);
        float phase = 0f;
        for (int i = 0; i < len && i < n; i++)
        {
            float u = i / (float)len;
            float f = RoeDeerF0 * (1f + (RoeDeerFall - 1f) * u) * (1f + RoeDeerJitter * jit[i]);
            phase += 2f * Mathf.PI * f / rate;

            float env = Mathf.Min(1f, u / RoeDeerAttack)
                        * Mathf.Exp(-RoeDeerDecay * u)
                        * Mathf.Min(1f, (1f - u) / RoeDeerRelease);

            var stack = new HarmonicStack(phase);
            float tone = 0f;
            float wsum = 0f;
            for (int k = 1; k <= RoeDeerHarmonics; k++)
            {
                float value = k == 1 ? stack.Current : stack.Next();
                float dev = (k * f - RoeDeerFormantHz) / RoeDeerFormantBw;
                float w = tilt[k - 1] / (1f + dev * dev);
                tone += w * value;
                wsum += w;
            }

            d[i] += env * (tone / Mathf.Max(wsum, 1e-6f) + RoeDeerNoise * nb[i]);
        }

        Normalise(d, RoeDeerPeak);
        return Finish("RoeDeer", d, rate);
    }

    // ---- 5. THE OWLET'S BEG -----------------------------------------------------------------

    /// <summary>THE TWO RASPS — (start, length, level). A fledged long-eared owl begs all night in
    /// July and August, and everyone who has walked a German wood in late summer has heard it: a
    /// long, thin, drawn-out "psiiiih" every few seconds, which is why the local name for it is a
    /// squeaky gate hinge. 1.20 s apart, which is far too long to read as a rhythm and is the reason
    /// this clip is the longest in the set at 1.86 s.</summary>
    private static readonly float[][] OwletRasps =
    {
        new[] { 0.000f, 0.50f, 1.00f },
        new[] { 1.200f, 0.44f, 0.80f },
    };

    /// <summary>The rasp's carrier and its downward inflection. 3350 Hz falling to 2915 (0.87): a
    /// beg SAGS, and a level one sounds like a smoke alarm. H2 at 0.18 and nothing above it — the
    /// tone in this call is thin by design, because the character is not in the tone.</summary>
    private const float OwletF0 = 3350f;
    private const float OwletFall = 0.87f;
    private const float OwletH2 = 0.18f;

    /// <summary>THE SQUEAK — a 42 Hz amplitude roughness at 30% depth. That is what makes this a
    /// hinge rather than a whistle, and 42 Hz on a 3.35 kHz carrier is a TIMBRE (sidebands at
    /// +-42 Hz, 1.2% of the carrier), which is a different thing entirely from a 30-45 Hz pulse on a
    /// low-mid carrier — the shape THE NIGHT CALLS rejected a frog for, and the one this file has
    /// deleted a bed over. Nothing here beats, because there is nothing low enough to beat.</summary>
    private const float OwletRaspHz = 42f;
    private const float OwletRaspDepth = 0.30f;

    /// <summary>THE NOISE IS THE SOUND. At 1.15 of the tone, this is the only call in the bank whose
    /// breath outweighs its voice, and that is what separates it from <see cref="NightBird"/> at the
    /// other end of a 0.21 octave gap: 0.48 octaves of spectral spread against the bird's 0.14. Two
    /// clips can sit at 3.0 and 3.5 kHz and still be unmistakable if one is a whistle and the other
    /// is a rasp.</summary>
    private const float OwletNoise = 1.15f;
    private const float OwletNoiseLoHz = 2500f;
    private const float OwletNoiseHiHz = 7500f;

    private const float OwletSeconds = 1.86f;
    private const float OwletPeak = 0.72f;
    private const uint OwletSeed = 0x1F3B8000u;

    /// <summary>A YOUNG LONG-EARED OWL BEGGING. Two long rasps, high and noisy.</summary>
    private static AudioClip MakeOwletBeg(int rate)
    {
        int n = (int)(rate * OwletSeconds);
        var d = new float[n];
        float[] nb = NoiseBand(n, rate, OwletSeed, OwletNoiseLoHz, OwletNoiseHiHz);

        for (int p = 0; p < OwletRasps.Length; p++)
        {
            int at = (int)(OwletRasps[p][0] * rate);
            int len = (int)(OwletRasps[p][1] * rate);
            float level = OwletRasps[p][2];
            if (len <= 0)
                continue;

            float phase = 0f;
            for (int i = 0; i < len && at + i < n; i++)
            {
                float u = i / (float)len;
                float f = OwletF0 * (1f + (OwletFall - 1f) * u);
                phase += 2f * Mathf.PI * f / rate;

                float env = Shoulders(u, 0.16f, 0.34f);
                env *= 1f - OwletRaspDepth * 0.5f
                            * (1f - Mathf.Cos(2f * Mathf.PI * OwletRaspHz * (i / (float)rate)));

                float tone = Mathf.Sin(phase) + OwletH2 * Mathf.Sin(2f * phase);
                d[at + i] += level * env * (tone + OwletNoise * nb[at + i]);
            }
        }

        Normalise(d, OwletPeak);
        return Finish("OwletBeg", d, rate);
    }

    // ---- the haunt cues ---------------------------------------------------------------------------

    /// <summary>
    /// Rope or old timber taking weight. A creak is STICK-SLIP: the load builds, the fibres let go
    /// a little, it builds again. So this is a train of short filtered bursts whose spacing
    /// SHORTENS as the load settles, under a slow swell — never one continuous groan, which is what
    /// makes a synthetic creak sound like a synthesizer.
    ///
    /// <para>The 0.90 shrink is what makes it a creak rather than a knock-knock-knock, and it is
    /// also what froze the game on the loading screen in ModBuild 145 when it drove a
    /// <c>while</c> condition instead of a fixed count — see <see cref="EnvSoundSchedule"/>, which
    /// now owns the schedule and cannot fail to reach the end of its window.</para>
    /// </summary>
    private static AudioClip MakeCreak(int rate)
    {
        int n = (int)(rate * 1.3f);
        var d = new float[n];
        var r = new Rng(0xC2EA00u);

        var slips = new float[16];
        EnvSoundSchedule.SlipTrain(slips, 0.05f, 1.20f, shrink: 0.90f, jitter: 0.25f, seed: 0xC2EA00u);
        for (int s = 0; s < slips.Length; s++)
        {
            float t = slips[s];
            int at = (int)(t * rate);
            float f = 210f + 130f * Mathf.Abs(r.Next());
            float amp = 0.35f + 0.65f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.2f));
            for (int i = 0; at + i < n && i < rate * 0.09f; i++)
            {
                float tt = i / (float)rate;
                d[at + i] += Mathf.Sin(2f * Mathf.PI * f * tt) * Mathf.Exp(-24f * tt) * amp * 0.5f;
                d[at + i] += r.Next() * Mathf.Exp(-70f * tt) * amp * 0.25f;
            }
        }

        LowPass(d, rate, 2400f);
        Normalise(d, 0.7f);
        return Finish("Creak", d, rate);
    }

    /// <summary>
    /// A breath that is not yours: one slow out-breath. Noise through a resonance that MOVES the
    /// way a throat does — that movement is the whole difference between a breath and a hiss, and
    /// it is why this is worth 30 lines rather than being a filtered-noise blip.
    /// </summary>
    private static AudioClip MakeBreath(int rate)
    {
        int n = (int)(rate * 1.0f);
        var d = new float[n];
        var r = new Rng(0xB2EA7u);

        // two swept one-pole resonators, tracked by hand so the sweep is per sample
        float y1 = 0f, y2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 1.0f;
            // the envelope of an out-breath: fast in, long out
            float env = Mathf.Min(1f, u / 0.14f) * Mathf.Exp(-2.1f * u);
            // 620 -> 380 Hz: the mouth closing
            float f = 620f - 240f * u;
            float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * f / rate));
            float w = r.Next();
            y1 += a * (w - y1);
            y2 += a * (y1 - y2);
            d[i] = (y2 * 2.6f + w * 0.10f) * env;
        }

        HighPass(d, rate, 140f);
        Normalise(d, 0.55f);
        return Finish("Breath", d, rate);
    }

    /// <summary>Cloth, or a wet palm, on stone. Band-limited noise whose band sweeps down as the
    /// contact slows, with a slight roughness from a low AM.</summary>
    private static AudioClip MakeDrag(int rate)
    {
        int n = (int)(rate * 1.4f);
        var d = new float[n];
        var r = new Rng(0xD2A6u);

        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 1.4f;
            float env = Mathf.Min(1f, u / 0.22f) * Mathf.Min(1f, (1f - u) / 0.30f);
            float f = 3200f - 2100f * u;                       // the band sliding down
            float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * f / rate));
            float w = r.Next();
            y += a * (w - y);
            float rough = 0.80f + 0.20f * Mathf.Sin(2f * Mathf.PI * 31f * t);
            d[i] = (w - y) * env * rough;                      // (w - y) is the high-passed part
        }

        HighPass(d, rate, 600f);
        Normalise(d, 0.5f);
        return Finish("Drag", d, rate);
    }

    /// <summary>One fly. A buzz is a harmonic-rich tone whose pitch WANDERS (the insect is
    /// manoeuvring) with amplitude that comes and goes as it turns.</summary>
    private static AudioClip MakeFly(int rate)
    {
        int n = (int)(rate * 1.6f);
        var d = new float[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 1.6f;
            float env = Mathf.Min(1f, u / 0.18f) * Mathf.Min(1f, (1f - u) / 0.22f);
            float f = 168f + 22f * Mathf.Sin(2f * Mathf.PI * 1.7f * t) + 11f * Mathf.Sin(2f * Mathf.PI * 4.3f * t);
            phase += 2f * Mathf.PI * f / rate;
            // a sawtooth-ish stack: wings are not a sine
            float s = Mathf.Sin(phase) + 0.5f * Mathf.Sin(phase * 2f) + 0.30f * Mathf.Sin(phase * 3f)
                      + 0.18f * Mathf.Sin(phase * 4f);
            float turn = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 0.9f * t + 1.1f);
            d[i] = s * env * turn * 0.3f;
        }

        Normalise(d, 0.45f);
        return Finish("Fly", d, rate);
    }

    // =============================================================================================
    //  THE BOOKSHELF'S IMPACT — rebuilt. ModBuild 148.
    // =============================================================================================
    //
    //  THE USER REPORT, verbatim: "Beim Umfallen des Regals sollte es schon ein Geräusch beim Impact
    //  auf dem Boden geben."  ("When the shelf falls over there really should be a sound at the
    //  impact on the floor.")
    //
    //  THERE ARE THREE INDEPENDENT REASONS HE HEARD NO IMPACT, and every one of them had to be
    //  fixed or the other two would still have hidden the result:
    //
    //    1. IT PLAYED 3.7 SECONDS TOO EARLY. This is the big one and it is not in this file — see
    //       EnvSound.TickHaunt. The cue fired at the event START plus a 0.15 s lead, and the clip
    //       put its thud 0.85 s in, so the thud landed ~1.0 s into an event whose shelf does not
    //       reach the floor until 4.68 s (EnvShelfTip.cginc: GHVR_TIP_FALL = 0.18 of a 26.002 s
    //       envelope). At 1.0 s the shelf has leaned about ONE DEGREE. A bang while a bookcase is
    //       still visibly upright does not read as an impact at all; it reads as "something else in
    //       the room made a noise", which is exactly the report.
    //    2. THE THUD WAS BELOW THE SPEAKERS. Two sines at 58 and 86 Hz behind a 420 Hz low pass,
    //       with nothing else in the buffer but a rustle. The rig is a Quest 3 over Virtual Desktop,
    //       and the Quest's own speakers are down hard below ~100 Hz — so on the hardware the report
    //       came from, more than half the clip's energy was inaudible BY CONSTRUCTION. The modes
    //       below sit at 78 and 135 Hz and the contact transient carries real content up to the low
    //       hundreds, so the event survives a small speaker.
    //    3. IT WAS A NOTE, NOT A THUD — the finding an earlier lane made and the reason the ear
    //       filed it under "sound effect" rather than "impact". 86 / 58 = 1.483, and an equal-
    //       tempered perfect fifth is 1.4983: the two partials were a fifth apart to within a fifth
    //       of a semitone. Worse, they shared one envelope, exp(-3.4t) — a 294 ms time constant, so
    //       the 58 Hz partial rang for SEVENTEEN CYCLES and the 86 Hz one for twenty-five. Pitch
    //       perception needs about four. A consonant interval held that long is a musical dyad, and
    //       the ear has no choice in the matter.
    //
    //  WHAT IT IS INSTEAD, as physics.
    //    * TWO CARCASS MODES IN AN INHARMONIC RATIO. sqrt(3) = 1.7320508 is 9.51 semitones — a
    //      quarter-tone off the major sixth below it and a quarter-tone off the minor seventh above
    //      it, i.e. as far from every simple interval as a ratio in that range can get. That is a
    //      deliberate anti-tuning and it is stated as one; the honest physical claim is only the
    //      weaker one, that a plywood-and-shelf carcass loaded with books has no reason to be
    //      harmonic and every reason not to be.
    //    * ...AND THEY BARELY RING, which matters more than the ratio. See FallCarcassQ: seven,
    //      derived once and applied to both modes, giving 2.2 cycles EACH (constant Q is constant
    //      cycles, which is why one number covers both). Under about four cycles there is no pitch
    //      to hear at all, so the interval question stops being decidable — which is a better fix
    //      than choosing a different interval, because it cannot be undone by a later retuning.
    //    * THE FLAGSTONE ANSWERS, at the SAME 160 Hz the drip's knock uses. There is one floor in
    //      this room and both impacts are hitting it; a slab's modal frequencies are a property of
    //      the slab and not of what fell on it, so the drip and the bookshelf agreeing about the
    //      floor is a correctness property, not a coincidence to be tuned away.
    //    * THE LOAD ARRIVES LATE. A carcass full of books is not one body: the boards stop first,
    //      the books keep going for another few centimetres and land on the shelves. That is a
    //      broadband slump 25 ms behind the contact, decaying over ~0.3 s, and it is the term that
    //      says "full bookcase" rather than "plank".
    //
    //  REJECTED:
    //    * KEEPING THE 0.85 s LEAN INSIDE THE CLIP and simply scheduling it 0.85 s early. It would
    //      work, and it would put a magic offset between two files that already have four numbers
    //      to keep in step. The clip is now an EVENT AT t = 0, which is a thing a scheduler can
    //      place; the run-up is a separate cue at a separate time (EnvSound.CueFor's card 5).
    //    * A CRASH — clatter, splintering, books tumbling. It is what a real bookcase does and it
    //      is ruled out by the standing rule twice over: the apparitions may never announce
    //      themselves, and this is the loudest thing in the environment. The 950 Hz ceiling keeps it
    //      a WUMPH.
    //    * DETUNING THE FIFTH AND LEAVING THE ENVELOPE. The interval is the symptom; the 294 ms
    //      decay is the disease. Any two partials held that long will be heard as an interval, and
    //      the next person to pick a ratio would be picking one for a chord that should not exist.

    /// <summary>
    /// The carcass's quality factor, and the ONE number both mode decays are derived from —
    /// the same discipline as <see cref="DripBubbleQ"/>.
    ///
    /// <para>SEVEN. A bookcase is plywood and pine with a hundred kilos of paper resting on it, and
    /// paper is about the most effective constrained-layer damper there is; loss factors for a
    /// loaded shelf are order 0.1, i.e. Q of order 5-10. alpha = pi*f/Q then puts the 78 Hz mode at
    /// a 28.6 ms time constant (2.2 cycles) and the 135 Hz mode at 16.5 ms (2.2 cycles as well —
    /// constant Q means constant CYCLES, which is why one number is enough for both). Under about
    /// four cycles the ear cannot extract a pitch, so this clip has a WEIGHT and no note. The
    /// shipped envelope was 294 ms flat for both partials: seventeen cycles and eleven.</para>
    /// </summary>
    private const float FallCarcassQ = 7f;

    /// <summary>The carcass's lowest mode, Hz, and the ratio of the second to it. 78 is chosen
    /// against the PLAYBACK as much as against the body: see reason 2 in the block comment — a
    /// headset speaker gives nothing back below ~100 Hz, so a thud whose fundamental is at 58 does
    /// not exist on the hardware the report came from.</summary>
    private const float FallModeHz = 78f;
    private const float FallModeRatio = 1.7320508f;

    /// <summary>The flagstone, hit hard. Deliberately the same slab as
    /// <c>MakeDripVariant</c>'s <c>stoneHz</c> band (152-175 Hz) and with the same Q — a floor's
    /// modes do not change with what lands on them.</summary>
    private const float FallSlabHz = 160f;
    private const float FallSlabTau = 0.0115f;

    /// <summary>The contact transient: 9 ms, so it is over in ~40. This is the "impact" the user
    /// asked for, and it is the loudest instant in the buffer by design — an arrival peaks on its
    /// contact, exactly as a drop does (see the plop-not-plink note in <see cref="MakeDrips"/>).
    /// </summary>
    private const float FallSlapTau = 0.009f;

    /// <summary>The books, arriving after the boards. 25 ms of lag and a 110 ms decay: the carcass
    /// stops, its load does not, and a few centimetres at arrival speed is tens of milliseconds.
    /// </summary>
    private const float FallLoadDelay = 0.025f;
    private const float FallLoadTau = 0.11f;

    // =============================================================================================
    //  THE CRACK AND THE SCATTER — ModBuild 149, and this is where the user's exception is spent.
    // =============================================================================================
    //
    //  USER RULING, verbatim: "Ich höre immer noch keine Impactsounds beim Bücherregal das umkippt -
    //  ich gebe dir hierbei eine Ausnahmegenehmigung hier auch einen lauten Knall Sound einzubauen in
    //  dem Moment in das Regal den Boden berührt."  ("I still hear NO impact sound when the bookshelf
    //  falls over — I am giving you a special permission here to build in a LOUD BANG at the moment
    //  the shelf touches the floor.")
    //
    //  HE HAD ALREADY BEEN GIVEN A FIX AND IT DID NOT REACH HIM, so the question is what was still
    //  wrong. ModBuild 148 moved the thud from 3.7 s early onto the shelf's real arrival and the log
    //  proves the schedule fires (Player.log:21575 prints all four contact times). The clip was then
    //  MEASURED off the finished buffer, outside Unity, against the same arithmetic and seed:
    //
    //      peak 0.8500, reached at 1.44 ms (90% of peak at 1.31 ms) — so the attack was NOT slow and
    //      the clip DID peak on its contact, which is the thing the last round set out to fix.
    //
    //      energy above  500 Hz:  2.69 %
    //      energy above  950 Hz:  1.99 %
    //      energy above 2000 Hz:  0.88 %
    //      spectral centroid:      184 Hz
    //
    //  THAT is the defect, and it is spectral rather than temporal. Ninety-seven per cent of the
    //  clip's energy sat below 500 Hz — two carcass modes at 78 and 135 Hz, a slab at 160, and a
    //  950 Hz one-pole ceiling over everything including the contact transient. The rig is a Quest 3
    //  over Virtual Desktop and its speakers give essentially nothing back below ~150-250 Hz, so the
    //  event was a sub-bass wumph played into a transducer that cannot make sub-bass, at a source
    //  gain of 0.080 x a master of 0.75 = 0.060 on Unity's scale. It was not too early and it was not
    //  attack-less. It was INAUDIBLE ON THE HARDWARE, twice over.
    //
    //  WHAT A BOOKCASE HITTING A STONE FLOOR ACTUALLY IS, and the two terms that were missing:
    //    * A BROADBAND CRACK. Wood striking stone is two hard surfaces meeting at a few m/s: the
    //      contact area is millimetres for the first instant, so the radiated spectrum is FLAT well
    //      into the kHz and the event announces itself in the band the ear is most sensitive in and
    //      a small speaker can actually reproduce. The 950 Hz ceiling deleted exactly that.
    //    * A SCATTER OF CONTENTS. Nine hard, small, irregular impacts spread over the third of a
    //      second behind the crack — the books and whatever else was on the shelves arriving one
    //      after another, heavy things first. It is the term that says "a full bookcase went over"
    //      rather than "a plank fell", and it costs nothing because it is nine 12 ms ticks.
    //  The BODY — modes, slab, load slump — is unchanged and still low-passed at 950 Hz. The two new
    //  terms are built in their OWN buffer and band-limited separately, which is why adding them
    //  does not require reopening the body's ceiling.
    //
    //  ...AND THE LEVEL IS NOT THIS FILE'S TO SET. The gain that finally makes it a bang is
    //  EnvSound.ShelfImpactGain, which is the only place in the feature allowed past
    //  EnvSound.MaxEmitterGain and which quotes the permission above. Clip shape and clip level are
    //  kept apart here for the same reason every other generator ends in Normalise: a generator that
    //  quietly ran hot would defeat the gain budget from underneath it, exception or no exception.
    //
    //  REJECTED:
    //    * OPENING THE BODY'S 950 Hz CEILING INSTEAD OF ADDING A SECOND BUFFER. It would have made
    //      the carcass modes' own harmonics audible, and those are the "note, not a thud" defect the
    //      last round removed. The crack is a different sound source from the carcass and gets its
    //      own band, which is also the physically honest split.
    //    * A CRASH — splintering, glass, a long clatter. Still ruled out. The permission is for a
    //      BANG at one instant, not for a demolition that runs for two seconds over the game.
    //    * MAKING THE WHOLE ENVIRONMENT LOUDER. The exception is for this one contact. Every other
    //      emitter is still under MaxEmitterGain and still ducks.

    /// <summary>Decay of the broadband crack, per the exponential's time constant. 1.6 ms, so the
    /// crack is 60 dB down inside 11 ms — shorter than the contact slap it sits on top of, because
    /// the crack is the instant the two surfaces meet and the slap is the millisecond after it.
    /// Under the class doc's non-masking budget this is the DURATION argument in its strongest form:
    /// nothing this short can mask a syllable.</summary>
    private const float FallCrackTau = 0.0016f;

    /// <summary>The crack's band — and, since ModBuild 150, the boards' band too, because they are
    /// the same event on the same buffer. Deliberately NOT the body's: 320 Hz keeps it out of the
    /// carcass modes so it reads as a separate surface rather than as brightness on the boom.
    ///
    /// <para>THE CEILING COMES DOWN, 7.2 kHz to 5.0. The old corner was justified as "where a
    /// wood-on-stone contact stops carrying useful information and starts carrying hiss", and the
    /// measurement says the corner was above that point rather than at it: 12.1% of the ModBuild 149
    /// clip's energy sat above 5 kHz, more than the 7.6% in the entire 1-5 kHz band where the
    /// headset speaker and the ear are both at their best. 5.0 kHz moves that energy down into the
    /// band that reaches the player instead of spending it on air.</para></summary>
    private const float FallCrackLoHz = 320f;
    private const float FallCrackHiHz = 5000f;

    /// <summary>The contents arriving: how many, and the window they land in. Nine over 0.035-0.42 s,
    /// which is a shelf's worth of books falling half a metre onto a floor that is already there.
    /// The times come from <see cref="EnvSoundSchedule.SlipTrain"/>, so this loop TERMINATES BY
    /// CONSTRUCTION — a <c>for</c> over an int fixed before it starts, with no accumulator in any
    /// condition. See EnvSoundSchedule.cs for the freeze that discipline exists to prevent.</summary>
    private const int FallScatterCount = 9;
    private const float FallScatterFirst = 0.035f;
    private const float FallScatterLast = 0.42f;

    /// <summary>The scatter's gaps WIDEN (&gt; 1), like the rat's do not and the creak's do the
    /// opposite of. The heavy things go first and together; what is left is lighter, tumbles further
    /// and arrives later, so the train thins out and stops rather than ending on a beat.</summary>
    private const float FallScatterSpread = 1.30f;

    /// <summary>Fraction each individual gap is randomly stretched or squeezed by. High, because a
    /// pile of objects arriving has no rhythm whatsoever.</summary>
    private const float FallScatterJitter = 0.70f;

    /// <summary>Decay of one scattered object's tick, and the power-law exponent on their sizes.
    /// The exponent is the same reasoning the drip's variants use: things that break loose from a
    /// falling structure are not all the same size, small ones vastly outnumber large ones, and
    /// <c>u^1.6</c> for uniform <c>u</c> is the inverse CDF that says so. A handful of the nine
    /// carry the scatter and the rest are barely there.</summary>
    private const float FallScatterTau = 0.0035f;
    private const float FallScatterExponent = 1.6f;

    /// <summary>How loud the two ModBuild 149 terms are built RELATIVE to the body, before the
    /// <see cref="Normalise"/> that ends the generator. These are the numbers that decide the
    /// clip's SPECTRUM rather than its level: raising them moves energy out of the 78-160 Hz band a
    /// headset speaker cannot reproduce and into the band it can.</summary>
    private const float FallCrackMix = 2.4f;
    private const float FallScatterMix = 1.1f;

    // =============================================================================================
    //  THE BOARDS — ModBuild 150, and this is the term that turns a TICK into a BANG.
    // =============================================================================================
    //
    //  USER RULING, verbatim, on the ModBuild 149 build: "Beim Impact vom Bücherregal kommt der Sound
    //  a) aus der falschen Stelle, b) viel zu leise, c) kein Knall wie man ihn erwarten würde."
    //  ("...c) not a bang the way you would expect one.")
    //
    //  ModBuild 149 said 19.6% of the energy was above 1 kHz and called that fixed. RE-MEASURED off
    //  the finished buffer, band by band, that number was hiding the actual shape:
    //
    //        0-200 Hz  72.7 %      <- still the great majority, and STILL the band a Quest 3
    //      200-500 Hz   5.2 %         speaker gives nothing back in
    //      500-1k Hz    2.5 %
    //        1-2k Hz    1.6 %
    //        2-5k Hz    5.9 %
    //       5-24k Hz   12.1 %      <- and most of what IS above 1 kHz is up HERE, where a
    //                                 wood-on-stone contact is hiss rather than information
    //      centroid 1824 Hz, energy in the 1-5 kHz band where the speaker and the ear are both
    //      at their best: SEVEN POINT SIX PER CENT.
    //
    //  So on the hardware the clip was a 1.6 ms click with nothing behind it, over a boom the
    //  transducer could not make. That is not a bang; a bang is a click WITH A BODY. And the body was
    //  missing because the only resonances in the clip were the carcass's bulk modes at 78 and
    //  135 Hz — the box as a whole — with a 950 Hz ceiling over everything else.
    //
    //  WHAT WAS ACTUALLY MISSING: THE BOARDS. A bookcase is not one body, it is a stack of thin
    //  panels, and when it lands they all flex at once. A shelf board is about 0.55 x 0.30 m of 18 mm
    //  pine; for a plate, f_mn = (pi/2) * sqrt(D/rho.h) * ((m/a)^2 + (n/b)^2), and pine's
    //  sqrt(E h^2 / 12 rho) is 23.2 m^2/s at that thickness. That puts its first five modes at
    //
    //      (1,1) 525    (2,1) 885    (3,1) 1489    (1,2) 1737    (2,2) 2098  Hz
    //
    //  which is exactly the 0.5-2 kHz band the whole event had nothing in. These are DERIVED, not
    //  chosen, and they are what a wooden bang sounds like: the low boom is the box, the crack is the
    //  contact, and this is the WOOD.
    //
    //  REJECTED:
    //    * SIMPLY RAISING FallCrackMix FURTHER. The crack is 1.6 ms of noise; more of it is a louder
    //      click, and a click is what the report says the clip already is.
    //    * MOVING THE CARCASS MODES UP. They are derived from the box's own dimensions and are the
    //      "weight" the event needs. The boards are a SECOND source, not a re-tuning of the first.
    //    * A LONGER CLIP. "Kein Knall" is about the first 50 ms. The clip is still 0.9 s and the
    //      scatter still ends inside 0.42 s; nothing here is a clatter.

    /// <summary>The shelf boards' first five plate modes, Hz — see the block comment for the plate
    /// formula they come out of. They are inharmonic by construction (the ratios are sums of squares,
    /// not integers), which is what keeps a wooden bang from having a NOTE.</summary>
    private static readonly float[] FallBoardHz = { 525f, 885f, 1489f, 1737f, 2098f };

    /// <summary>Q of those modes. TEN, and it is chosen the same way <see cref="FallCarcassQ"/> is:
    /// constant Q means constant CYCLES, and Q/pi = 3.2 cycles for every one of the five. Under about
    /// four cycles the ear cannot extract a pitch, so the boards give the bang WEIGHT and no note —
    /// the same test the carcass's own modes are held to, applied to a panel that is stiffer, thinner
    /// and (with books on it) just as heavily damped. In time constants: 6.1 ms at 525 Hz down to
    /// 1.5 ms at 2098 Hz.</summary>
    private const float FallBoardQ = 10f;

    /// <summary>How fast a board reaches its full amplitude. A plate mode is not excited by a
    /// mathematical delta — the contact force builds over the millisecond or two the surfaces take to
    /// conform — and this is that rise.
    ///
    /// <para>IT IS ALSO WHAT KEEPS THE PEAK WHERE IT BELONGS. With no rise, five modes starting at
    /// phase zero reach a coherent maximum at t = 0 together with the crack, and the whole clip's
    /// peak (which is what <see cref="Normalise"/> and therefore the entire level budget is measured
    /// against) gets spent on a coincidence. With it, the crack owns the first millisecond and the
    /// boards own the twenty after it, which is both the honest physics and the envelope of a
    /// bang.</para></summary>
    private const float FallBoardRise = 0.0015f;

    /// <summary>How loud the boards are built relative to the body, and the tilt across them.
    ///
    /// <para>THE TILT IS NOT TASTE. At constant Q a mode's energy goes as amplitude^2 / f, so equal
    /// amplitudes would put four times the energy in the lowest mode as in the highest and the clip
    /// would ring at 525 Hz. <c>amp ∝ sqrt(f)</c> — i.e. <c>(f/f0)^0.5</c>, written below as the
    /// reciprocal power — makes the ENERGY flat across the five, which is what a broadband impulse
    /// striking a plate actually deposits.</para></summary>
    private const float FallBoardMix = 2.5f;
    private const float FallBoardTilt = 0.5f;

    /// <summary>How hard the finished buffer is saturated — see <see cref="SoftClip"/>, which is
    /// where the whole argument for having a saturator at all is written down. 3.0 recovers about
    /// 5 dB of the 15 dB crest factor and generates the harmonics that let a headset speaker imply a
    /// 78 Hz mode it cannot reproduce.</summary>
    private const float FallDrive = 3.0f;

    /// <summary>
    /// THE BOOKSHELF ARRIVING. The impact is at <c>t = 0</c>: this clip is an EVENT, and the caller
    /// puts it on the frame the shelf actually reaches the floor (EnvSound.TickHaunt, from
    /// <c>EnvShelfTip.cginc</c>'s own phase constants). Everything before the arrival — the carcass
    /// creaking as it commits to the lean — is a different cue at a different time.
    ///
    /// <para>Played a second time, quieter and slightly sharper, for the ballistic rebound's second
    /// contact 0.624 s later; the shelf really does touch the floor twice and the curve says exactly
    /// when. See EnvSound's shelf schedule.</para>
    ///
    /// <para><b>MEASURED, off the finished buffers</b> (generated outside Unity against the same
    /// arithmetic and the same seed, and cross-checked: the replica reproduces ModBuild 149's peak,
    /// attack, RMS and centroid to the digit. The DEVICE's own figures for peak and attack are
    /// printed by <c>EnvSound</c>'s shelf-contact log line, from <see cref="MeasuredShape"/>):</para>
    /// <code>
    ///                    peak    peak at   90% of peak     RMS   E&gt;1kHz  centroid   -20 dB in
    ///   NEW (150)       0.980    2.44 ms      0.19 ms    0.0697   33.2%   1530 Hz       27 ms
    ///   ModBuild 149    0.980    1.31 ms      1.04 ms    0.0455   19.6%   1824 Hz       40 ms
    ///   ModBuild 148    0.850    1.44 ms      1.31 ms    0.0548    1.9%    184 Hz          -
    ///   SHIPPED (147)   0.850     854 ms          -      0.1195      -         -          -
    ///
    ///   energy by band          0-200  200-500  500-1k   1-2k   2-5k  5-24k    1-5 kHz
    ///   NEW (150)               26.9%     5.3%   34.5%  17.3%  10.0%   5.9%      27.3%
    ///   ModBuild 149            72.7%     5.2%    2.5%   1.6%   5.9%  12.1%       7.6%
    /// </code>
    /// <para><b>WHAT EACH ROUND ACTUALLY FIXED, so the next one does not re-fix a solved half.</b>
    /// ModBuild 148 moved the peak from 854 ms to 1.4 ms, so the clip has PEAKED ON ITS CONTACT ever
    /// since and the ATTACK HAS NEVER BEEN THE DEFECT — 90% of peak in 1.0 ms was not a slow attack
    /// and neither is 0.19 ms. Nor was the decay: 40 ms to -20 dB is already a bang's envelope rather
    /// than a thump's. ModBuild 149 then moved the centroid, and its own headline number (19.6% above
    /// 1 kHz) is true and was not enough: 72.7% of the energy was still under 200 Hz where the
    /// hardware gives nothing back, and of what WAS above 1 kHz more sat above 5 kHz than in the
    /// whole 1-5 kHz band. The clip was a click over an inaudible boom.</para>
    ///
    /// <para>ModBuild 150 adds the term that was missing — THE BOARDS, five derived plate modes
    /// between 525 and 2098 Hz (see THE BOARDS) — brings the contact's ceiling down from 7.2 kHz to
    /// 5.0, trims the two sub-audible carcass modes by 3.3 dB, and ends the generator in
    /// <see cref="SoftClip"/>. The 1-5 kHz band goes from 7.6% to 27.3% and the sub-200 Hz share from
    /// 72.7% to 26.9%. The number that matters most is neither of those: through a one-pole 200 Hz
    /// high pass — a crude stand-in for the Quest 3's own low-end rolloff — the LOUDEST 20 ms WINDOW,
    /// which is roughly what the ear integrates an impact over, goes from 0.157 to <b>0.369, i.e.
    /// +7.4 dB, at exactly the same peak sample</b>. On top of that
    /// <c>EnvSound.ShelfImpactGain</c> and the rolloff fix beside it add another 10.6 dB of level
    /// that the clip's own peak cannot show.</para>
    /// </summary>
    private static AudioClip MakeFall(int rate)
    {
        // 0.9 s, and it is a bound: the longest term is the load slump at 110 ms, which is 100 dB
        // down by 0.62 s. HALF the shipped length, because the shipped clip spent 0.85 s of it on a
        // run-up that is now a separate cue — so this rebuild makes the bank cheaper, not dearer.
        int n = (int)(rate * 0.9f);
        var d = new float[n];
        // TWO BUFFERS, because they are two different sources with two different bands: `d` is the
        // BODY (the carcass, the slab, the load) and keeps its 950 Hz ceiling, `h` is the CRACK and
        // the SCATTER and gets a band of its own. They are summed once, at the end, and the single
        // Normalise then decides the clip's peak exactly as it does for every other generator.
        var h = new float[n];
        var r = new Rng(0xFA11u);

        // alpha = pi*f/Q for both modes, from the one Q. Derived, not tuned.
        float hi = FallModeHz * FallModeRatio;
        float aLo = Mathf.PI * FallModeHz / FallCarcassQ;
        float aHi = Mathf.PI * hi / FallCarcassQ;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;

            // ---- 1. THE CONTACT. Zero-mean noise, full amplitude at t = 0: an impact IS a
            // discontinuity and must not have an attack. This is the loudest instant in the buffer.
            float s = t < 0.06f ? r.Next() * Mathf.Exp(-t / FallSlapTau) * 0.90f : 0f;

            // ---- 2. THE FLAGSTONE, struck. sin(wt)*exp(-t/tau) starting at sin(0) = 0 is the
            // correct phase for something excited by a blow at t = 0, not a fade.
            if (t < 0.09f)
                s += Mathf.Sin(2f * Mathf.PI * FallSlabHz * t) * Mathf.Exp(-t / FallSlabTau) * 0.55f;

            // ---- 3. THE CARCASS. Two modes, inharmonic, and both dead inside three cycles.
            //
            // BUILT 3.3 dB QUIETER than ModBuild 149 (0.80/0.42 -> 0.55/0.30), and the reason is the
            // limiter at the bottom of this method rather than the physics. These two modes are the
            // clip's biggest EXCURSION and they are at 78 and 135 Hz, which a Quest 3 speaker turns
            // into nothing; every decibel of headroom they take is a decibel the saturator has to
            // give back out of the parts that DO reach the player. They are still the largest single
            // band in the finished clip (26.9% under 200 Hz) — this is the weight being kept in
            // proportion, not removed.
            if (t < 0.35f)
            {
                s += Mathf.Sin(2f * Mathf.PI * FallModeHz * t) * Mathf.Exp(-aLo * t) * 0.55f;
                s += Mathf.Sin(2f * Mathf.PI * hi * t) * Mathf.Exp(-aHi * t) * 0.30f;
            }

            // ---- 4. THE LOAD. Broadband, late, and soft-edged — books do not click.
            float tl = t - FallLoadDelay;
            if (tl > 0f && tl < 0.55f)
                s += r.Next() * (1f - Mathf.Exp(-tl * 180f)) * Mathf.Exp(-tl / FallLoadTau) * 0.30f;

            d[i] = s;
        }

        // ---- 5. THE CRACK. The instant the two hard surfaces meet, and the term the ModBuild 148
        // clip did not have at all. Full amplitude at t = 0 with no rise whatsoever — a contact is a
        // discontinuity — and gone inside 11 ms. Its own Rng, so adding it does not shift a single
        // draw in the body above and the boom is bit-identical to the clip that was measured.
        var hr = new Rng(0xC7ACu);
        int crackLen = (int)(rate * 0.02f);
        for (int i = 0; i < crackLen && i < n; i++)
        {
            float t = i / (float)rate;
            h[i] += hr.Next() * Mathf.Exp(-t / FallCrackTau) * FallCrackMix;
        }

        // ---- 5b. THE BOARDS. The shelves and the side panels flexing, which is the term that makes
        // this a BANG rather than a click over an inaudible boom — see THE BOARDS above for the plate
        // formula the five frequencies come out of and for the measurement that says why it was
        // needed. It sits in the CONTACT buffer, not the body's, because it belongs to the contact's
        // band and not to the carcass's 950 Hz ceiling; and it draws NOTHING from `hr`, so the crack
        // above and the scatter below keep the draw sequence they were measured with.
        //
        // 0.12 s of buffer is a bound, not a fade: the longest of the five has a 6.1 ms time constant
        // and is 170 dB down by then.
        int boardLen = (int)(rate * 0.12f);
        for (int m = 0; m < FallBoardHz.Length; m++)
        {
            float f = FallBoardHz[m];
            float aB = Mathf.PI * f / FallBoardQ;
            // amp ∝ sqrt(f) — flat ENERGY across the five at constant Q. See FallBoardTilt.
            float amp = FallBoardMix * Mathf.Pow(f / FallBoardHz[0], FallBoardTilt);
            for (int i = 0; i < boardLen && i < n; i++)
            {
                float t = i / (float)rate;
                h[i] += Mathf.Sin(2f * Mathf.PI * f * t)
                        * (1f - Mathf.Exp(-t / FallBoardRise))
                        * Mathf.Exp(-aB * t) * amp;
            }
        }

        // ---- 6. THE SCATTER. The contents arriving behind the carcass. The times come from
        // SlipTrain — a for over a fixed count, gaps normalised onto the window afterwards — so this
        // cannot spin whatever the spread and jitter are; see EnvSoundSchedule.cs.
        var thrown = new float[FallScatterCount];
        EnvSoundSchedule.SlipTrain(thrown, FallScatterFirst, FallScatterLast,
                                   shrink: FallScatterSpread, jitter: FallScatterJitter, seed: 0xC7ACu);
        int tickLen = (int)(rate * 0.012f);
        for (int k = 0; k < thrown.Length; k++)
        {
            int at = (int)(thrown[k] * rate);
            // Power-law sizes: Abs() of the -1..1 draw is uniform on [0,1), and raising it to the
            // exponent is that distribution's inverse CDF. The statistics ARE the line.
            float amp = Mathf.Pow(Mathf.Abs(hr.Next()), FallScatterExponent);
            for (int i = 0; i < tickLen && at + i < n; i++)
            {
                float tt = i / (float)rate;
                h[at + i] += hr.Next() * Mathf.Exp(-tt / FallScatterTau) * amp * FallScatterMix;
            }
        }

        // The crack's own band, applied to the crack's own buffer — this is what lets the body keep
        // a 950 Hz ceiling while the contact reaches into the kHz where a headset speaker lives.
        HighPass(h, rate, FallCrackLoHz);
        LowPass(h, rate, FallCrackHiHz);

        // THE CEILING. 950 Hz, raised from the shipped 420. The old corner was justified as "a
        // cellar's worth of air between it and the ear" and that justification does not survive
        // contact with the picture: the room is a DIORAMA the player is leaning over, at two to
        // twenty-four perceived metres, not a sound from another building. 420 Hz removed
        // everything that says "wood" and left a pure boom. 950 keeps the contact's edge and still
        // guarantees no clatter — there is nothing of ours in the 1-4 kHz speech band, which is the
        // class doc's rule and the reason this is not simply opened up.
        LowPass(d, rate, 950f);
        // ...and the floor, below the lowest real mode: DC out, sub-audible excursion out.
        HighPass(d, rate, 55f);

        // ONE SUM, THEN THE SATURATOR, THEN ONE NORMALISE. The peak of the finished buffer is still
        // the contact, which is what an arrival on a stone floor peaks on. 0.98 rather than the
        // body's old 0.85: this is the one clip the user has explicitly asked to be loud, and leaving
        // 15% of headroom unused in the BUFFER would only have to be bought back in the gain, where
        // it is capped.
        //
        // SoftClip is the ONE mastering step in this file and it is spent here, on the one clip that
        // carries a written exception. Its own doc comment carries the argument and the measurement;
        // the short version is that the peak this normalise sets was a single noise sample 15 dB
        // above everything the ear actually integrates, so the level budget was being spent on
        // something inaudible.
        for (int i = 0; i < n; i++)
            d[i] += h[i];
        SoftClip(d, FallDrive);
        Normalise(d, 0.98f);
        return Finish("Fall", d, rate);
    }

    /// <summary>Seconds from the start of <see cref="Settle"/> to its one contact with the floor.
    ///
    /// <para>1.162 IS READ OFF THE CURVE, not chosen. <c>EnvShelfTip.cginc</c> plays the recovery as
    /// the fall run backwards at 0.537x, so the fall's 0.624 s rebound window becomes a 1.162 s one
    /// — and run backwards it means the shelf ROCKS UP 1.9 degrees over the first 0.58 s of the
    /// recovery, comes back down, TOUCHES, and only then lifts off. That touch is the single audible
    /// contact in the whole righting, and it is at 1.162 s. The shipped clip put a "comes to rest"
    /// thump at 2.35 s, which under this curve is a bang while the shelf is already in the air.</para>
    /// </summary>
    private const float SettleContactSeconds = 1.162f;

    /// <summary>...and the righting. Slower, quieter, and with its one contact 1.162 s in — the
    /// mirror of the fall's rebound, because the recovery IS the fall reversed (see
    /// <see cref="SettleContactSeconds"/>). A thing standing itself back up is the half you are not
    /// supposed to be comfortable with.</summary>
    private static AudioClip MakeSettle(int rate)
    {
        int n = (int)(rate * 2.8f);
        var d = new float[n];
        var r = new Rng(0x5E77u);

        // The same carcass on the same floor, so the same mode and the same Q as MakeFall — gently.
        float aLo = Mathf.PI * FallModeHz / FallCarcassQ;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            // a long, uneven grind of wood on stone
            float grind = 0.35f + 0.25f * Mathf.Sin(2f * Mathf.PI * 3.1f * t) + 0.18f * Mathf.Sin(2f * Mathf.PI * 7.7f * t);

            // TWO PHASES, and the seam between them is the contact. Before it the shelf is rocking
            // up onto its edge and back — the noise swells quadratically, which is what a body
            // rolling on a corner under a rising load does. After it the shelf leaves the floor and
            // the contact patch shrinks to nothing, so the grind dies away.
            float body = t < SettleContactSeconds
                ? (t / SettleContactSeconds) * (t / SettleContactSeconds) * 0.62f
                : Mathf.Exp(-(t - SettleContactSeconds) * 1.45f);
            d[i] += r.Next() * grind * body * 0.30f;

            if (t >= SettleContactSeconds && t < SettleContactSeconds + 0.30f)
            {
                float tt = t - SettleContactSeconds;
                d[i] += Mathf.Sin(2f * Mathf.PI * FallModeHz * tt) * Mathf.Exp(-aLo * tt) * 0.32f;
            }
        }

        LowPass(d, rate, 520f);
        Normalise(d, 0.5f);
        return Finish("Settle", d, rate);
    }
}

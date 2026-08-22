"""THE ROOM TONES THAT WERE DELETED, AND WHAT REPLACED THEM — MakeStone and
MakeNightAir (gone from the bank as of ModBuild 223, kept HERE as the before
column), MakeOwl, MakeNightBird and MakeChirr from src/GloomhavenVR/Core/
EnvSound.Bank.cs, sample for sample, plus every level EnvSound plays them at.

===========================================================================
ModBuild 223 — THE DESIGN REVERSAL. READ THIS FIRST.
===========================================================================

ModBuild 221 added two continuous room tones; 222 rebuilt them. The user has
now rejected the WHOLE APPROACH on hardware. Verbatim, 2026-08-22:

    "2) Im Keller hören sich die Geräusche an wie Rauschen bei nem Fernseher.
     Es soll dezenter sein nicht aufdringliuch und auf keinen Fall nervig."

    "3) Auch die kontinuierlichen Sounds im Wald nerven mich. Statt generrell
     durchgehende sounds zu machen lieber die Tierrufe und im Wald ein ganz
     leiser dezenter Windzug. Mach die meistens Sounds an eine Quelle in der
     Welt hörbar. zB der Windzug im Keller könnte vom Fenster ausgehen (aber
     auch hier nur dezent!). Im Wald mal ne Eule oder ähnliches die ruft (auch
     aus dem Wald hörbar, hier sollte die Position auch random wechseln)."

So the two beds are DELETED — clip, generator, constants, dials — and this
file's job changed with them. It is no longer "are the room tones right"; it is
TWO questions, and `static_report()` and `rest_report()` are the two answers:

  1. WAS IT MEASURABLY TV STATIC? "Rauschen wie beim Fernseher" is not an
     opinion once you write down what static IS: a FLAT spectrum (a 1/3-octave
     spectral flatness measure near 1), spread over a WIDE band, with NO
     envelope structure (autocorrelation near zero AND a small envelope swing).
     `static_report()` prints all four against band-limited white noise, which
     is literally what a detuned analogue set puts out. The ModBuild 222 stone
     bed scores worse on every one of them than anything else the bank plays.
  2. IS WHAT REMAINS QUIETER? `rest_report()` is the complete inventory of what
     a player hears with NOTHING infused and nothing happening, per room, per
     emitter, delivered at 4 perceived metres through both high-pass corners.

THE MB221/MB222 GENERATORS ARE KEPT under `mb221_` and `make_stone` /
`make_night_air`. They are the BEFORE column of every table in this round's
comments, and a deletion whose before-column has been deleted is a deletion
nobody can check. THEY NO LONGER MIRROR ANY SHIPPED CODE — that is the point.

The earlier record, kept because the two rounds below were decided by
measurement against confident wrong guesses and must not be re-derived:

USER REQUEST, 2026-08-22, verbatim:

    "In Szenarios gibt es immer die dortigen Hintergrundgeräusche deswegen ist
     mir die Stille vorher nie aufgefallen. Im Keller scheint es constant
     geräusche zu geben, im Wald hingegen ist es absolut still. Ich will für
     beide eine dezente Hintergrundgeräuschkullise die zu der Umgebung passt.
     Diese soll deaktivierbar sein."

...and the HARDWARE VERDICT on the round that answered it (ModBuild 221), which
is what this file now has to measure, verbatim:

    "1) Die Soundkulisse im Wald ist viel zu nervig. Hört sich an als ob im
     Hintergrund ein Trasktor fährt. Dezenter Wind kann bleiben und ansonsten
     eventuell hier und da noch ein ruf von tieren (was man so im Wald in der
     Nacht hört), aber aktuell ist da irgendwas nicht in Ordnung"

    "3) Im Keller höre ich im Gegensatz zum Wald kaum was außer die
     effektgeräusche zB von der Maus."

TWO ROOMS, THE SAME BED DESIGN AT THE SAME GAIN, AND OPPOSITE VERDICTS. That is
the whole shape of the problem and it is what `tractor_report()` below settles:

  * THE PRIME SUSPECT WAS THE LEAF LITTER — 22 baked ticks in a 13 s loop, ~1.7
    per second, on a low wash: physically an idling engine. IT IS FALSIFIED HERE.
    `litter_control()` renders the ModBuild 221 marsh with the ticks and without
    them and the two are indistinguishable: the band split moves by 0.1
    percentage points and the envelope autocorrelation does not move at all
    (0.053 vs 0.056 — the tick-free version is the HIGHER of the two). At
    MarshTickMix = 1.4 the litter never left the wash.
  * WHAT DOES CONVICT IS THE BAND. `tractor_report()` prints the 200-500 Hz share
    and the spectral spread: the ModBuild 221 marsh is 53.0% in 200-500 Hz at
    0.68 octaves of spread, i.e. a NARROW, STATIONARY, LOW-MID band of noise —
    which is what distant machinery is — and it was the only thing in the wood
    below 2200 Hz, so nothing masked it.
  * ...AND MASKING WAS NOT THE CELLAR'S ANSWER EITHER. `masking_report()` prints
    the delivered level through a 500 Hz high pass, because that is the band a
    Quest 3 speaker actually returns: the ModBuild 221 stone bed reaches 0.00286
    there against ONE candle bed's 0.00084 — the thing he could not hear was
    10.6 dB LOUDER than the thing he could. It was authored below the chain, not
    masked. (An earlier draft of this docstring quoted the candle at 0.00607 and
    concluded the opposite; that figure omitted the candle's 0.6 m rolloff
    minimum, which costs it 16.5 dB by the time the player is at the table.)

    python3 bank.py     the validation case (Fall), then the wind bed
    python3 fire.py     the three fire layers
    python3 candle.py   candle vs wind vs roar
    python3 room.py     this: the static verdict on the two DELETED room tones,
                        then the full at-rest inventory that replaced them, then
                        the ModBuild 221/222 record kept for the diff

===========================================================================
ModBuild 226 — "DIESER 'REGEN' SOUND". THE THIRD ROUND ON THE SAME EMITTER.
===========================================================================

USER, hardware, verbatim, and it is two sentences of which the second is the
constraint that decides the shape of the fix:

    "Im Wald gefällt mir nur dieser 'Regen' Sound nicht der ab und zu kommt
     und für eine Zeit bleibt, ansonsten finde ich es sehr gut."

"ab und zu kommt und für eine Zeit bleibt" is a SCHEDULE, not a timbre, and
exactly one emitter in the wood has that schedule by construction: the INSECT
CHORUS. `chorus_schedule()` drives EnvSound's own arithmetic — Haunt.Hash's
cascade, character for character — over an hour of shared clock and prints the
realised arrivals and lengths, so "ab und zu" and "für eine Zeit" are numbers
rather than a reading of his sentence.

`rain_report()` is the falsification, and it adds ONE instrument this file did
not have: a RAIN CONTROL. `static_report()`'s control is band-limited WHITE
noise, which is a television; rain on leaves is the same broadband hiss with a
tilt and a slower envelope, so it needs a control of its own or "sounds like
rain" stays an opinion. Every forest emitter is measured against BOTH controls
on the four columns that separate a hiss from a sound with a source: 1/3-octave
spectral flatness, spectral spread, the share above 2 kHz, and the envelope.

AND IT RENDERS THE FOREST WITH AND WITHOUT THE SUSPECT, which is the instrument
the ModBuild 222 round asked for by name and the one that killed the leaf
litter. `forest_mix()` sums every continuous forest emitter at its DELIVERED
weight (gain x modulator x rolloff at 4 perceived metres) and measures the sum
— because the player does not hear a clip, he hears a room.

THE ALTERNATIVE THAT HAD TO BE FALSIFIED, and it is a real one rather than a
courtesy: the LEAVES bed under an AIR infusion. It also "comes now and then and
stays for a while" (the wind gate opens over 0.9 s, holds for the infusion and
closes over 2.4 s) and it also gets much louder when it does (+18.3 dB from the
resting floor). It is falsified by BAND, not by level — see rain_report().

EVERY CONSTANT BELOW EITHER MIRRORS ONE IN THE SHIPPED SOURCE (the `make_chirr`
/ `make_owl` / `make_bird` clip constants, and the ModBuild 223 level block) OR
IS A DELETED ONE KEPT AS A BEFORE COLUMN (`make_stone`, `make_night_air`,
everything `mb221_`, everything `MB222_`). The first kind must move when the
source moves, or the tables in EnvSound's comments stop being measurements and
become claims. The second kind must never move again — it is a record.
"""
import math
import numpy as np
from bank import (Rng, low_pass, high_pass, normalise, loop_fade, slip_train,
                  poisson_gap, make_bed, as_heard, measure, show, RATE, PI)


def white(n, seed):
    r = Rng(seed)
    return np.array([r.next() for _ in range(n)])


# ---- the cellar's stone bed — DELETED AT ModBuild 223, KEPT AS THE BEFORE ---
# These constants no longer mirror anything: EnvSoundBank.MakeStone and every
# constant under it are gone. This is the buffer the user heard when he wrote
# "Rauschen bei nem Fernseher", and static_report() is what convicts it.
StoneSeconds = 11.0
StonePink = (60.0, 240.0, 900.0)
StonePinkMix = (0.58, 0.30, 0.12)
StoneLoHz = 280.0          # MB221: 230
StoneHiHz = 520.0
StoneLoPoles = 3
StoneHiPoles = 3
StoneHissLoHz = 2200.0     # MB221: 4000
StoneHissHiHz = 9000.0     # MB221: 8000
StoneHissPoles = 2
StoneHissMix = 0.55        # MB221: 0.14
StonePeak = 0.72
StoneSeed = 0x5704E000


def make_stone(rate=RATE, lo=None, hisslo=None, hisshi=None, hissmix=None):
    lo = StoneLoHz if lo is None else lo
    hisslo = StoneHissLoHz if hisslo is None else hisslo
    hisshi = StoneHissHiHz if hisshi is None else hisshi
    hissmix = StoneHissMix if hissmix is None else hissmix

    n = int(rate * StoneSeconds)
    d = np.zeros(n)

    # ---- the BODY: pink-ish noise, MakeBed's three summed one-poles.
    r = Rng(StoneSeed)
    k1 = 1 - math.exp(-2 * PI * StonePink[0] / rate)
    k2 = 1 - math.exp(-2 * PI * StonePink[1] / rate)
    k3 = 1 - math.exp(-2 * PI * StonePink[2] / rate)
    a1 = a2 = a3 = 0.0
    for i in range(n):
        w = r.next()
        a1 += k1 * (w - a1)
        a2 += k2 * (w - a2)
        a3 += k3 * (w - a3)
        d[i] = a1 * StonePinkMix[0] + a2 * StonePinkMix[1] + a3 * StonePinkMix[2]
    for _ in range(StoneLoPoles):
        high_pass(d, rate, lo)
    for _ in range(StoneHiPoles):
        low_pass(d, rate, StoneHiHz)
    normalise(d, 1.0)

    # ---- the HISS CEILING: an INDEPENDENT white stream. THIS IS WHERE THE
    # CELLAR'S AUDIBILITY NOW LIVES — see masking_report: the body is below the
    # band a headset speaker returns, so raising the body is spending energy the
    # player cannot hear, and raising it far enough to be heard is exactly what
    # made the wood a tractor.
    h = white(n, StoneSeed ^ 0x48155000)
    for _ in range(StoneHissPoles):
        high_pass(h, rate, hisslo)
    for _ in range(StoneHissPoles):
        low_pass(h, rate, hisshi)
    normalise(h, 1.0)
    d += h * hissmix

    loop_fade(d, rate // 2)
    normalise(d, StonePeak)
    return d


def mb221_stone(rate=RATE):
    """The ModBuild 221 stone bed — the BEFORE column."""
    return make_stone(rate, lo=230.0, hisslo=4000.0, hisshi=8000.0, hissmix=0.14)


# ---- the wood's night air — DELETED AT ModBuild 223, KEPT AS THE BEFORE -----
#
# It fixed the tractor and was rejected anyway, for being continuous at all:
# "Auch die kontinuierlichen Sounds im Wald nerven mich."
#
# THE ENGINE BAND IS GONE BY CONSTRUCTION. Three poles of high pass at 700 Hz
# leave 0.7% of the buffer in 200-500 Hz against the MB221 wash's 53.0%.
NightAirSeconds = 17.0
NightAirLoHz = 700.0
NightAirHiHz = 2600.0
NightAirLoPoles = 3
NightAirHiPoles = 3
NightAirTopLoHz = 4500.0
NightAirTopHiHz = 11000.0
NightAirTopPoles = 2
NightAirTopMix = 0.16
NightAirPeak = 0.62
NightAirSeed = 0x3A25E000


def make_night_air(rate=RATE):
    n = int(rate * NightAirSeconds)

    # WHITE, not the pink of MakeBed/MakeStone. Pink RISES as it goes down and
    # everything this clip is trying not to be is down there; the band filters
    # then shape it, and starting from a flat stream is the only way three poles
    # of high pass actually reach 0.7% in 200-500.
    d = white(n, NightAirSeed)
    for _ in range(NightAirLoPoles):
        high_pass(d, rate, NightAirLoHz)
    for _ in range(NightAirHiPoles):
        low_pass(d, rate, NightAirHiHz)
    normalise(d, 1.0)

    t = white(n, NightAirSeed ^ 0x51A70000)
    for _ in range(NightAirTopPoles):
        high_pass(t, rate, NightAirTopLoHz)
    for _ in range(NightAirTopPoles):
        low_pass(t, rate, NightAirTopHiHz)
    normalise(t, 1.0)
    d += t * NightAirTopMix

    loop_fade(d, rate // 2)
    normalise(d, NightAirPeak)
    return d


# ---- the ModBuild 221 marsh, kept as the BEFORE column ----------------------
MarshSeconds = 13.0
MarshPink = (80.0, 340.0, 1300.0)
MarshPinkMix = (0.50, 0.32, 0.18)
MarshLoHz = 210.0
MarshHiHz = 1000.0
MarshLoPoles = 2
MarshHiPoles = 3
MarshTicks = 22
MarshTickFirst = 0.11
MarshTickLast = 12.85
MarshTickSpread = 1.0
MarshTickJitter = 0.95
MarshTickTau = 0.0016
MarshTickExponent = 1.5
MarshTickMix = 1.4
MarshPeak = 0.68
MarshSeed = 0x3A25E000


def mb221_marsh(rate=RATE, ticks=MarshTicks):
    """The ModBuild 221 wood bed. `ticks=0` is the CONTROL that falsifies the
    leaf litter as the cause of the tractor — see litter_control()."""
    n = int(rate * MarshSeconds)
    d = np.zeros(n)

    r = Rng(MarshSeed)
    k1 = 1 - math.exp(-2 * PI * MarshPink[0] / rate)
    k2 = 1 - math.exp(-2 * PI * MarshPink[1] / rate)
    k3 = 1 - math.exp(-2 * PI * MarshPink[2] / rate)
    a1 = a2 = a3 = 0.0
    for i in range(n):
        w = r.next()
        a1 += k1 * (w - a1)
        a2 += k2 * (w - a2)
        a3 += k3 * (w - a3)
        d[i] = a1 * MarshPinkMix[0] + a2 * MarshPinkMix[1] + a3 * MarshPinkMix[2]

    if ticks:
        at_s = slip_train(ticks, MarshTickFirst, MarshTickLast,
                          MarshTickSpread, MarshTickJitter, MarshSeed)
        tick_len = int(rate * 0.008)
        tr = Rng(MarshSeed ^ 0x7C1C0000)
        for k in range(ticks):
            at = int(at_s[k] * rate)
            amp = abs(tr.next()) ** MarshTickExponent * MarshTickMix
            for i in range(tick_len):
                if at + i >= n:
                    break
                tt = i / rate
                d[at + i] += tr.next() * math.exp(-tt / MarshTickTau) * amp

    for _ in range(MarshLoPoles):
        high_pass(d, rate, MarshLoHz)
    for _ in range(MarshHiPoles):
        low_pass(d, rate, MarshHiHz)

    loop_fade(d, rate // 2)
    normalise(d, MarshPeak)
    return d


# ---- the two night calls ---------------------------------------------------
#
# "eventuell hier und da noch ein ruf von tieren (was man so im Wald in der Nacht
# hört)". A tawny owl and a small bird further off. Both are EVENTS, which is the
# whole reason the owl is allowed to live in the band the wash was evicted from —
# see tractor_report's last block.
OwlSeconds = 2.30
OwlF0 = 395.0
OwlFall = 0.93
OwlH2 = 0.20
OwlH3 = 0.06
OwlBreath = 0.055
OwlBreathLoHz = 300.0
OwlBreathHiHz = 1200.0
OwlTremHz = 13.7
OwlTremDepth = 0.34
OwlPhrases = ((0.00, 0.52, 1.00, False),
              (0.94, 0.13, 0.55, False),
              (1.28, 0.92, 0.90, True))
OwlPeak = 0.80
OwlSeed = 0x71B4C000


def make_owl(rate=RATE):
    n = int(rate * OwlSeconds)
    d = np.zeros(n)

    nb = white(n, OwlSeed)
    high_pass(nb, rate, OwlBreathLoHz)
    low_pass(nb, rate, OwlBreathHiHz)
    normalise(nb, 1.0)

    for (st, ln, lv, trem) in OwlPhrases:
        a = int(st * rate)
        m = int(ln * rate)
        ph = 0.0
        for i in range(m):
            if a + i >= n:
                break
            u = i / m
            f = OwlF0 * (1.0 + (OwlFall - 1.0) * u)
            ph += 2 * PI * f / rate
            env = min(1.0, u / 0.11) * min(1.0, (1.0 - u) / 0.24)
            env = env * env * (3 - 2 * env)
            if trem:
                g = max(0.0, (u - 0.42) / 0.58)
                env *= 1.0 - OwlTremDepth * g * 0.5 * (1 - math.cos(2 * PI * OwlTremHz * (i / rate)))
            s = math.sin(ph) + OwlH2 * math.sin(2 * ph) + OwlH3 * math.sin(3 * ph)
            d[a + i] += lv * env * (s + OwlBreath * nb[a + i])

    normalise(d, OwlPeak)
    return d


BirdSeconds = 0.78
BirdF0 = 2760.0
BirdRise = 1.13
BirdH2 = 0.13
BirdStep = 0.035
BirdNotes = ((0.000, 0.085, 1.00),
             (0.235, 0.080, 0.86),
             (0.470, 0.090, 0.72))
BirdPeak = 0.78
BirdSeed = 0x4E7D3000


def make_bird(rate=RATE):
    n = int(rate * BirdSeconds)
    d = np.zeros(n)
    for k, (st, ln, lv) in enumerate(BirdNotes):
        a = int(st * rate)
        m = int(ln * rate)
        ph = 0.0
        f0 = BirdF0 * (1.0 + BirdStep * k)
        for i in range(m):
            if a + i >= n:
                break
            u = i / m
            f = f0 * (1.0 + (BirdRise - 1.0) * u)
            ph += 2 * PI * f / rate
            env = 0.5 - 0.5 * math.cos(2 * PI * u)
            d[a + i] += lv * env * (math.sin(ph) + BirdH2 * math.sin(2 * ph))
    normalise(d, BirdPeak)
    return d


# ---- the insect floor ------------------------------------------------------
#
# THIS IS THE CLIP THE TRACTOR TURNED OUT TO BE IN. mb221_chirr's modulator is
# three summed sines whose rates RE-ALIGN: analytically its own normalised
# autocorrelation is +0.970 at a lag of 0.290 s and -0.993 at 0.145 s, i.e. a
# hard beat at 3.45 Hz with a perfect anti-phase at the half period. Off the
# finished buffer auto_corr() measures 0.867 at 0.291 s, against 0.05-0.12 for
# every other bed in the bank. ModBuild 221 did not create it — it moved the
# runtime corner from 1150 to 3000 Hz and lifted the gain 1.5x, which UNBURIED a
# metronome that had been in the clip since it was written.
ChirrSeconds = 7
ChirrLoHz = 2200.0
ChirrHiHz = 6500.0
ChirrModLoHz = 3.0
ChirrModHiHz = 30.0
ChirrModPoles = 2
ChirrFloor = 0.20
ChirrDepth = 0.80
ChirrPeak = 0.55
ChirrSeed = 0xC317F


def mb221_chirr(rate=RATE):
    """EnvSoundBank.MakeChirr as it shipped in ModBuild 221 and every build before
    it — the BEFORE column, and the defect."""
    n = rate * ChirrSeconds
    d = np.zeros(n)
    r = Rng(ChirrSeed)
    for i in range(n):
        t = i / rate
        m = (0.55
             + 0.20 * math.sin(2 * PI * 17.3 * t)
             + 0.14 * math.sin(2 * PI * 23.9 * t)
             + 0.11 * math.sin(2 * PI * 31.1 * t))
        d[i] = r.next() * m
    high_pass(d, rate, ChirrLoHz)
    low_pass(d, rate, ChirrHiHz)
    loop_fade(d, rate // 2)
    normalise(d, ChirrPeak)
    return d


def make_chirr(rate=RATE):
    """ModBuild 222: the same carrier and the same band, modulated by BAND-LIMITED
    NOISE instead of by three sines. Any finite sum of sines re-aligns somewhere;
    noise has an autocorrelation that decays with its own bandwidth, so it is zero
    at every lag the ear could call a rhythm BY CONSTRUCTION."""
    n = rate * ChirrSeconds

    m = white(n, ChirrSeed ^ 0x2C41B000)
    for _ in range(ChirrModPoles):
        low_pass(m, rate, ChirrModHiHz)
    for _ in range(ChirrModPoles):
        high_pass(m, rate, ChirrModLoHz)
    normalise(m, 1.0)

    d = white(n, ChirrSeed) * (ChirrFloor + ChirrDepth * (0.5 + 0.5 * m))
    high_pass(d, rate, ChirrLoHz)
    low_pass(d, rate, ChirrHiHz)
    loop_fade(d, rate // 2)
    normalise(d, ChirrPeak)
    return d


def beat_report(cache):
    """THE MEASURED CAUSE OF THE TRACTOR, with its own analytic cross-check."""
    freqs, amps = (17.3, 23.9, 31.1), (0.20, 0.14, 0.11)
    p = np.array(amps) ** 2
    print("           the MB221 modulator, analytically:")
    for tau in (0.145, 0.290, 0.580):
        v = sum(p[i] * math.cos(2 * PI * freqs[i] * tau) for i in range(3)) / p.sum()
        print(f"             AC({tau:.3f} s = {1/tau:5.2f} Hz) = {v:+.3f}")
    for nm, d in (("MB221 chirr", cache["mb221chirr"]), ("223 chirr", cache["chirr"])):
        ac, lag = auto_corr(d, lo_s=0.05, hi_s=6.0)
        print(f"           {nm:<12} envelope AC {ac:.4f} @ {lag:.3f} s "
              f"({1/lag:5.2f} Hz)   envelope swing {env_swing(d):5.2f} dB   "
              f"bands " + " ".join(f"{x:5.1f}" for x in bands(d)))
    print("           => the band split is IDENTICAL and the rhythm is not. No spectral")
    print("              instrument could have found this; the round that looks for it")
    print("              has to measure TIME.")


# ---- EnvSound's side: what everything is PLAYED at -------------------------
#
# THE `MB222_` NAMES ARE THE DELETED BUILD. They are what the user heard when he
# wrote "Rauschen bei nem Fernseher" and "die kontinuierlichen Sounds im Wald
# nerven mich", and they are kept as the before column of static_report().
MB222_StoneGain = 0.045        # MB221: 0.035 — DELETED at 223
MB222_NightAirGain = 0.021     # MB221 (Marsh): 0.035 — DELETED at 223
RoomToneMinMeters = 5.5        # the deleted beds' rolloff minimum
MB222_ChirrLift = 1.15         # EnvSound.InsectAmbienceLift — DELETED at 223
ChirrLiftMB221 = 1.50
InsectLowPassHz = 3000.0       # KEPT: the 221 corner repair was never in dispute
ChirrGainOld = 0.050

# ---- ModBuild 223: what a player actually hears at rest ---------------------
#
# THE WIND RULING, AS THIS ROUND READS IT (the source says the same thing
# verbatim, in EnvSound.WindBed): the standing ruling was "Wind Geräusch nur
# wenn auch Wind aktiv ist, sonst kein Geräusch". Sentence 3 above now asks for
# "im Wald ein ganz leiser dezenter Windzug" at rest and for a draught from the
# cellar WINDOW ("aber auch hier nur dezent"). So the ruling is not withdrawn,
# it is NARROWED: a very quiet draught is now always present in both rooms and
# the Air infusion is the RISE above it. One floor, both rooms, one function.
WindRestFloor = 0.22       # EnvSound.WindRestFloor — the modulator at rest
DraughtGain = 0.075        # cellar window, unchanged
DraughtMinMeters = 1.2
LeavesGain = 0.070         # forest canopy, unchanged
LeavesMinMeters = 2.0

# The insect floor stops being continuous. It is a CHORUS on the shared clock:
# 55% of 53 s slots carry one, 14..30 s long, ramped in and out over 5 s, and
# EXACTLY ZERO in between (which pauses the source). Peak modulator 0.60 against
# the MB222 bed's steady ~0.90, and the x1.15 lift is gone with the dial it rode.
NightBedGain = 0.050
InsectChorusPeak = 0.60
InsectChorusSlot = 53.0
InsectChorusShare = 0.55
InsectChorusLenLo = 14.0
InsectChorusLenHi = 30.0

# The two calls now move between trees, so their ROLLOFF MINIMUM carries them
# instead of their gain — see EnvSound.OwlMinMeters. Perch ring 7..12 authored
# metres (owl) and 10..18 (bird) against the bake's ClearR = 5.4 m of open
# ground, i.e. 6.1..10.4 and 8.7..15.6 PERCEIVED m at the cellar's measured
# 0.866 perceived metres per authored metre.
OwlGain = 0.050            # MB222: 0.075
OwlMinMeters = 8.0         # MB222: 2.5
BirdGain = 0.032           # MB222: 0.060
BirdMinMeters = 10.0       # MB222: 2.0
OwlPerchNear, OwlPerchFar = 7.0, 12.0        # authored metres
BirdPerchNear, BirdPerchFar = 10.0, 18.0
AuthoredToPerceived = 0.866                  # measured, ModBuild 151 session


# ---- measurement -----------------------------------------------------------
def bands(d, rate=RATE):
    s = np.abs(np.fft.rfft(d)) ** 2
    f = np.fft.rfftfreq(len(d), 1.0 / rate)
    tot = float(np.sum(s))
    edges = [0, 200, 500, 1000, 2000, 5000, 24000]
    return [100.0 * float(np.sum(s[(f >= edges[i]) & (f < edges[i + 1])])) / tot
            for i in range(6)]


def audible(d, rate=RATE, lo=200.0, hi=12000.0):
    """candle.py's centroid + SPREAD over the band a Quest 3 returns. The spread
    is the number that says "a band" against "a rush"."""
    s = np.abs(np.fft.rfft(d)) ** 2
    f = np.fft.rfftfreq(len(d), 1.0 / rate)
    m = (f >= lo) & (f < hi)
    s, f = s[m], f[m]
    p = s / np.sum(s)
    oct_ = np.log2(f / 1000.0)
    c = float(np.sum(oct_ * p))
    spread = float(math.sqrt(np.sum(((oct_ - c) ** 2) * p)))
    return 1000.0 * (2.0 ** c), spread


def envelope(d, rate=RATE, win_ms=20.0):
    w = max(1, int(rate * win_ms / 1000.0))
    env = np.sqrt(np.convolve(d * d, np.ones(w) / w, mode="same"))
    env = env[w:-w]
    m = float(np.mean(env))
    return env / m if m > 1e-9 else env


def env_swing(d, rate=RATE):
    env = envelope(d, rate)
    lo, hi = float(np.percentile(env, 5)), float(np.percentile(env, 95))
    return 20.0 * math.log10(hi / max(lo, 1e-9))


def auto_corr(d, rate=RATE, reps=4, win_ms=8.0, dec=60, lo_s=0.15, hi_s=3.0):
    """THE ENGINE TEST, and the one the ModBuild 222 brief asked for by name: an
    engine has a SHARP autocorrelation peak at its firing rate and still air has
    none.

    The signal is the loop played `reps` times, so a rhythm that only exists
    because the buffer wraps is inside the analysed stream rather than outside
    it. The envelope window is 8 ms — the length of one baked leaf tick, so a
    tick train cannot hide under the smoothing — decimated to 800 Hz. Returns
    the largest normalised envelope autocorrelation over 0.15..3.0 s of lag and
    the lag it sits at.
    """
    x = np.tile(d, reps)
    w = max(1, int(rate * win_ms / 1000.0))
    e = np.sqrt(np.convolve(x * x, np.ones(w) / w, mode="same"))
    e = e[:len(e) // dec * dec].reshape(-1, dec).mean(axis=1)
    fs = rate / dec
    e = e - e.mean()
    n = len(e)
    a = np.correlate(e, e, mode="full")[n - 1:]
    a = a / a[0]
    lags = np.arange(len(a)) / fs
    m = (lags >= lo_s) & (lags <= hi_s)
    i = int(np.argmax(a[m]))
    return float(a[m][i]), float(lags[m][i])


def flatness(d, rate=RATE, lo=200.0, hi=12000.0, per_oct=3):
    """THE STATIC TEST, and the reason it is 1/3-OCTAVE and not raw FFT bins.

    "Rauschen wie beim Fernseher" is a claim about SHAPE: a detuned analogue set
    puts out band-limited white noise, whose spectrum is FLAT. The standard
    measure of that is the spectral flatness measure — the geometric mean of the
    power spectrum over its arithmetic mean, 1.0 for perfectly flat and falling
    toward 0 as the energy concentrates into a band.

    Taken on RAW periodogram bins it is useless here: a periodogram of white
    noise is chi-square-scattered bin to bin, so even true static scores ~0.56
    and the number says more about the estimator than about the signal. Averaged
    into 1/3-octave bands FIRST — which is roughly how the ear integrates, and is
    what every acoustics text means by the measure — white noise returns ~1.00
    and a narrow band returns a small fraction. static_report() prints white
    noise as its own control so the scale is on the page rather than asserted.
    """
    s = np.abs(np.fft.rfft(d)) ** 2
    f = np.fft.rfftfreq(len(d), 1.0 / rate)
    step = 2.0 ** (1.0 / per_oct)
    edges, e = [], lo
    while e < hi:
        edges.append(e)
        e *= step
    edges.append(hi)
    p = []
    for i in range(len(edges) - 1):
        m = (f >= edges[i]) & (f < edges[i + 1])
        if not np.any(m):
            continue
        p.append(float(np.mean(s[m])))
    p = np.array(p)
    p = p[p > 0]
    if len(p) < 2:
        return float("nan")
    return float(np.exp(np.mean(np.log(p))) / np.mean(p))


def band_energy(d, gain, rate=RATE, lo=1000.0, hi=5000.0):
    """ABSOLUTE energy an emitter puts into a band: the buffer's share times the
    square of the gain EnvSound plays it at. A share is not a level."""
    s = np.abs(np.fft.rfft(d)) ** 2
    f = np.fft.rfftfreq(len(d), 1.0 / rate)
    mean = float(np.sum(s[(f >= lo) & (f < hi)]) / len(d) ** 2)
    return mean * gain * gain


def w20(d, hz, rate=RATE):
    """Loudest 20 ms RMS through a TWO-POLE high pass at `hz`. Two corners are
    quoted everywhere below and the pair is the point: 200 Hz is the floor this
    project has always used as its stand-in for a Quest 3, and 500 Hz brackets
    it from the other side, because a small open-ear driver keeps losing output
    well above 200. A bed whose two numbers are far apart is a bed most of whose
    energy the player never receives — which is the cellar's whole story."""
    w = max(1, int(rate * 0.020))
    x = d.copy()
    for _ in range(2):
        high_pass(x, rate, hz)
    return float(np.max(np.sqrt(np.convolve(x * x, np.ones(w) / w, mode="same"))))


def delivered(d, gain, mod=0.93, minm=RoomToneMinMeters, at=4.0, hz=200.0):
    """What reaches the ear: the 20 ms/high-passed window, times the gain, times
    the mean runtime contour, times Unity's min/d rolloff (flat inside min)."""
    return w20(d, hz) * gain * mod * min(1.0, minm / at)


def line(label, d, gain, mod=0.93, minm=RoomToneMinMeters):
    b = bands(d)
    c, sp = audible(d)
    ac, lag = auto_corr(d)
    print(f"{label:<22} " + " ".join(f"{x:5.1f}" for x in b) +
          f" | {c:5.0f} Hz {sp:.2f} oct | AC {ac:.4f} @ {lag:.2f}s | "
          f"200Hz {delivered(d, gain, mod, minm):.5f} 500Hz "
          f"{delivered(d, gain, mod, minm, hz=500.0):.5f} | 1-5k {band_energy(d, gain):.3e}")


# ---- ModBuild 223: the two reports this round exists to print ---------------
def static_report(cache):
    """WAS IT MEASURABLY TV STATIC? Four columns, one control.

    THE CONTROL IS BAND-LIMITED WHITE NOISE — literally what a detuned analogue
    television puts out — so "flat" and "no envelope" have a scale on the page.
    A signal is static to the degree that it matches the control on ALL FOUR:
    flat (SFM -> 1), wide (spread, octaves), no rhythm (envelope AC -> 0) and no
    dynamics (envelope swing -> the ~2.5 dB a 20 ms window sees on pure noise).
    """
    # ELEVEN SECONDS, not four, and the length is load-bearing: auto_corr tiles
    # the buffer `reps` times, so a control shorter than the 6 s lag window
    # reports its OWN TILE PERIOD as a rhythm (a 4 s control scored 0.74 at a
    # 4.00 s lag, which is the instrument measuring itself). 11 s matches the
    # bed it is the control for.
    ctl = white(int(RATE * StoneSeconds), 0x7E1E7157)
    for _ in range(2):
        high_pass(ctl, RATE, 300.0)
    for _ in range(2):
        low_pass(ctl, RATE, 9000.0)
    normalise(ctl, 0.72)

    print("                        SFM   spread    env AC @ lag   env swing   >2 kHz   "
          "delivered@4m 500Hz")
    rows = [
        ("TV static (control)", ctl, None, None, None),
        ("MB222 Stone  DELETED", cache["stone"], MB222_StoneGain, 0.93, RoomToneMinMeters),
        ("MB222 NightAir DELET", cache["nightair"], MB222_NightAirGain, 0.92, RoomToneMinMeters),
        ("MB221 Stone", cache["mb221stone"], 0.035, 0.93, RoomToneMinMeters),
        ("MB221 Marsh", cache["mb221marsh"], 0.035, 0.92, RoomToneMinMeters),
        ("Candle flutter (kept)", cache["flutter"], 0.055, 0.86, 0.6),
        ("Draught, Air up (kept)", cache["bedheard"], DraughtGain, 1.80, DraughtMinMeters),
        ("Chirr, chorus peak", cache["chirr3k"], NightBedGain, InsectChorusPeak, 3.0),
    ]
    for label, d, g, mod, minm in rows:
        _, sp = audible(d)
        ac, lag = auto_corr(d, lo_s=0.05, hi_s=6.0)
        b = bands(d)
        hi = b[4] + b[5]
        dl = "        -" if g is None else f"{delivered(d, g, mod, minm, hz=500.0):9.5f}"
        print(f"{label:<23} {flatness(d):.3f}  {sp:.2f} oct  {ac:.4f} @ {lag:5.2f}s   "
              f"{env_swing(d):5.2f} dB   {hi:5.1f}%   {dl}")
    print()
    print("           THE VERDICT, and it is a decided question rather than an opinion:")
    print("           the ModBuild 222 stone bed is the FLATTEST, WIDEST, most envelope-free")
    print("           thing this bank has ever played, and it is also the LOUDEST thing in the")
    print("           cellar at rest by 15.5 dB over a candle bed. On all four columns it sits")
    print("           between every other bed and the white-noise control. Its 2200..9000 Hz")
    print("           hiss ceiling at StoneHissMix = 0.55 IS band-limited white noise — that is")
    print("           what the constant says — and ModBuild 222 raised it from 0.14 while")
    print("           chasing 'im Keller höre ich kaum was'. It over-corrected into a television.")


def rest_report(cache):
    """WHAT A PLAYER HEARS AT REST — nothing infused, nothing happening — after
    the deletion. Every continuous emitter in each room, in one place, so the
    next hardware report can name an emitter instead of a room."""
    print("  CELLAR                          node            gain    mod   min m   "
          "200Hz     500Hz     1-5k abs")
    def row(label, node, d, g, mod, minm):
        print(f"  {label:<30} {node:<15} {g:.3f}  {mod:5.2f}  {minm:5.1f}   "
              f"{delivered(d, g, mod, minm):.5f}   {delivered(d, g, mod, minm, hz=500.0):.5f}   "
              f"{band_energy(d, g * mod):.3e}")

    row("Draught (AT REST)", "WindowGlow", cache["bedheard"], DraughtGain,
        WindRestFloor, DraughtMinMeters)
    for i in range(3):
        row(f"Flame{i} (candle, unchanged)", f"Candles{i}", cache["flutter"], 0.055, 0.86, 0.6)
    print("  Stone room tone                 -               DELETED — this is the whole round")
    print()
    print("  FOREST                          node            gain    mod   min m   "
          "200Hz     500Hz     1-5k abs")
    row("Leaves (AT REST)", "Canopy", cache["bedheard"], LeavesGain,
        WindRestFloor, LeavesMinMeters)
    row("Night insects, CHORUS PEAK", "Ground", cache["chirr3k"], NightBedGain,
        InsectChorusPeak, 3.0)
    duty = InsectChorusShare * (0.5 * (InsectChorusLenLo + InsectChorusLenHi)) / InsectChorusSlot
    print(f"     ^^^ DELETED AT ModBuild 226 — this row is the BEFORE column and nothing plays it")
    print(f"         any more. It is the sound the user called rain: it carried a chorus in "
          f"{100*InsectChorusShare:.0f}% of")
    print(f"         {InsectChorusSlot:.0f} s slots, {InsectChorusLenLo:.0f}..{InsectChorusLenHi:.0f} s "
          f"long, i.e. a {100*duty:.0f}% duty cycle — \"ab und zu kommt und für eine")
    print("         Zeit bleibt\". See rain_report() and chorus_schedule() for the verdict.")
    print("  NightAir room tone              -               DELETED at ModBuild 223")
    print("  ...SO THE WOOD AT REST IS ONE EMITTER: the canopy draught, and nothing else at all.")
    print()
    print("  THE CALLS (events, ~1 per 53 s, position redrawn every call):")
    for nm, d, g, minm, near, far in (
            ("Owl", cache["owl"], OwlGain, OwlMinMeters, OwlPerchNear, OwlPerchFar),
            ("NightBird", cache["bird"], BirdGain, BirdMinMeters, BirdPerchNear, BirdPerchFar)):
        pn, pf = near * AuthoredToPerceived, far * AuthoredToPerceived
        lo_ = w20(d, 500.0) * g * min(1.0, minm / pf)
        hi_ = w20(d, 500.0) * g * min(1.0, minm / pn)
        print(f"  {nm:<12} gain {g:.3f}  rolloff min {minm:.0f} m   perch ring "
              f"{near:.0f}..{far:.0f} authored m = {pn:.1f}..{pf:.1f} perceived m   "
              f"delivered {lo_:.5f}..{hi_:.5f}")
    print()
    print("  AGAINST WHAT HE REJECTED (delivered @4m through 500 Hz, continuous emitters only):")
    reject = [("MB222 Stone", cache["stone"], MB222_StoneGain, 0.93, RoomToneMinMeters),
              ("MB222 NightAir", cache["nightair"], MB222_NightAirGain, 0.92, RoomToneMinMeters),
              ("MB222 Chirr", cache["chirr3k"], NightBedGain * MB222_ChirrLift, 0.90, 3.0)]
    keep = [("223 Draught at rest", cache["bedheard"], DraughtGain, WindRestFloor, DraughtMinMeters),
            ("223 Leaves at rest", cache["bedheard"], LeavesGain, WindRestFloor, LeavesMinMeters),
            # The chorus's own "after" is SILENCE as of ModBuild 226, so this row's second
            # column is the 223 state it was in when he rejected it a third time. The dB in
            # the last column is therefore the improvement 223 made and NOT this round's,
            # which is total. rain_report() is where this round's numbers are.
            ("223 Chirr chorus peak (NOW DELETED)", cache["chirr3k"], NightBedGain,
             InsectChorusPeak, 3.0)]
    for (an, ad, ag, am, ai), (bn, bd, bg, bm, bi) in zip(reject, keep):
        a = delivered(ad, ag, am, ai, hz=500.0)
        b = delivered(bd, bg, bm, bi, hz=500.0)
        print(f"    {an:<16} {a:.5f}  ->  {bn:<22} {b:.5f}   ({20*math.log10(b/a):+5.1f} dB)")


# =============================================================================
#  ModBuild 226 — "dieser 'Regen' Sound der ab und zu kommt und für eine Zeit
#  bleibt". THE SCHEDULE, THE CONTROL, AND THE WITH/WITHOUT.
# =============================================================================

def haunt_hash(n, k):
    """Haunt.H, the cascade EnvSound draws every scheduled decision from
    (Core/Haunt.Schedule.cs:112-118), which is itself the C# mirror of
    GhvrHauntH in EnvHaunt.cginc.

    IN DOUBLE, NOT FLOAT32, AND THAT IS STATED RATHER THAN HIDDEN: the shipped
    cascade is single precision and its association is load-bearing on the GPU
    side. This replica is used ONLY for population statistics (how often a
    chorus arrives, how long it runs), where a handful of borderline draws
    landing on the other side of a threshold moves a percentage by well under
    its own quantisation. It must NEVER be used to predict which slot a
    particular client will fire — that is what the C# mirror is for."""
    def frac(x):
        return x - math.floor(x)
    x = frac((n + 1.0 + k * 7.13) * 0.7548776662)
    x = frac(x * (x + 31.70))
    x = frac(x * (x + 17.31))
    return frac(x * (x + 43.19))


# EnvSound's chorus constants, mirrored. See EnvSound.InsectChorusSlot.
InsectChorusEdgeSeconds = 5.0
InsectChorusOnChannel = 5.0
InsectChorusLenChannel = 0.0


def chorus_schedule(hours=1.0):
    """"AB UND ZU KOMMT UND FÜR EINE ZEIT BLEIBT", AS NUMBERS. Drives the
    shipped schedule over an hour of shared clock and prints what a player
    actually experiences: how many choruses, how long each one holds, and how
    long the gaps between them are."""
    slots = int(hours * 3600 / InsectChorusSlot)
    runs, gaps, last_end = [], [], None
    for n in range(slots):
        if haunt_hash(n, InsectChorusOnChannel) >= InsectChorusShare:
            continue
        length = InsectChorusLenLo + (InsectChorusLenHi - InsectChorusLenLo) * \
            haunt_hash(n, InsectChorusLenChannel)
        start = n * InsectChorusSlot + 0.5 * (InsectChorusSlot - length)
        runs.append(length)
        if last_end is not None:
            gaps.append(start - last_end)
        last_end = start + length
    runs = np.array(runs)
    gaps = np.array(gaps)
    duty = runs.sum() / (hours * 3600)
    print(f"           over {hours:.0f} h of shared clock: {len(runs)} choruses in "
          f"{slots} slots ({100*len(runs)/slots:.0f}% of slots carry one)")
    print(f"           EACH ONE HOLDS   {runs.min():5.1f} .. {runs.max():5.1f} s, "
          f"mean {runs.mean():5.1f} s   <- \"für eine Zeit bleibt\"")
    print(f"           THE GAPS BETWEEN  {gaps.min():5.1f} .. {gaps.max():5.1f} s, "
          f"mean {gaps.mean():5.1f} s   <- \"ab und zu kommt\"")
    print(f"           duty cycle {100*duty:.0f}% — so the wood he says he likes IS the "
          f"other {100*(1-duty):.0f}%.")
    print("           NOTHING ELSE IN THE WOOD HAS THIS SHAPE. The two calls are 2.30 s and")
    print("           0.78 s one-shots; the draught never stops and never changes; the fires")
    print("           and the rumble need an infusion; the apparitions are on 83 s and are")
    print("           three cards of which one is silent. An emitter that arrives, holds for")
    print("           half a minute and leaves again is this one.")


def rain_control(rate=RATE):
    """LIGHT RAIN ON LEAVES, as a control. static_report()'s control is a
    television — band-limited WHITE noise, flat and wide. Rain is the same
    broadband hiss with a TILT (the drop-size distribution puts its body in
    1-8 kHz and rolls the bottom off) and a slower envelope. Without a control
    of its own, "es klingt wie Regen" stays an opinion; with one, every column
    below has a scale at both ends."""
    n = int(rate * ChirrSeconds)
    d = white(n, 0x2A19FA11)
    for _ in range(2):
        high_pass(d, rate, 900.0)
    low_pass(d, rate, 9000.0)
    # The slow swell of a shower arriving and passing — 0.1-1.5 Hz, which is
    # what separates rain from a tuner between stations.
    m = white(n, 0x2A19FA12)
    for _ in range(2):
        low_pass(m, rate, 1.5)
    high_pass(m, rate, 0.1)
    normalise(m, 1.0)
    d *= (0.65 + 0.35 * (0.5 + 0.5 * m))
    loop_fade(d, rate // 2)
    normalise(d, 0.60)
    return d


def _fit(d, n):
    return np.tile(d, int(math.ceil(n / len(d))))[:n]


# What every CONTINUOUS forest emitter contributes to the mix at 4 perceived
# metres: (gain, modulator, rolloff minimum). The weight is
# gain x mod x min(1, min/4), i.e. exactly what EnvSound.PlayShot/AddBed and
# Unity's logarithmic rolloff put on the buffer before it reaches the ear.
def _weight(gain, mod, minm, at=4.0):
    return gain * mod * min(1.0, minm / at)


def forest_mix(cache, chorus=True, air=0.0):
    """THE WOOD AS ONE SIGNAL. The player does not hear a clip, he hears a room,
    so the with/without has to be measured on the SUM."""
    n = int(RATE * ChirrSeconds)
    leaf_mod = WindRestFloor if air <= 0 else (WindRestFloor
                                               + (1.0 - 0.5 * WindRestFloor)
                                               * (0.55 + 0.45 * 0.5 + 1.0 * air))
    mix = _fit(cache["bedheard"], n) * _weight(LeavesGain, leaf_mod, LeavesMinMeters)
    if chorus:
        mix = mix + _fit(cache["chirr3k"], n) * _weight(NightBedGain, InsectChorusPeak, 3.0)
    return mix


def make_rumble(rate=RATE):
    """EnvSoundBank.MakeRumble (EnvSound.Bank.cs:1871), sample for sample. It is
    on this table for one reason: it is the last forest emitter that comes and
    goes, and a suspect nobody measured is a suspect nobody excluded."""
    n = int(rate * 6)
    r = Rng(0xEA27F)
    t = np.arange(n) / rate
    d = (np.sin(2 * PI * 41.0 * t) * 0.5 + np.sin(2 * PI * 47.5 * t) * 0.35
         + np.array([r.next() for _ in range(n)]) * 0.5)
    low_pass(d, rate, 110.0)
    loop_fade(d, rate // 2)
    normalise(d, 0.8)
    return d


def rain_report(cache):
    """DID THE INSECT CHORUS DO IT? Two controls, every forest emitter, and the
    wood rendered with the suspect and without it."""
    ctl_rain = rain_control()
    ctl_tv = white(int(RATE * ChirrSeconds), 0x7E1E7157)
    for _ in range(2):
        high_pass(ctl_tv, RATE, 300.0)
    for _ in range(2):
        low_pass(ctl_tv, RATE, 9000.0)
    normalise(ctl_tv, 0.72)

    print("  PER EMITTER          SFM   spread   >2 kHz   centroid   env AC @ lag   swing   "
          "delivered@4m")
    rows = [
        ("RAIN control", ctl_rain, None),
        ("TV static control", ctl_tv, None),
        ("Insect chorus, peak", cache["chirr3k"], _weight(NightBedGain, InsectChorusPeak, 3.0)),
        ("Leaves, at rest", cache["bedheard"], _weight(LeavesGain, WindRestFloor, LeavesMinMeters)),
        ("Leaves, AIR FULL", cache["bedheard"], _weight(LeavesGain, 1.80, LeavesMinMeters)),
        ("Fire roar (Fire up)", cache["roar"], _weight(0.16, 1.0, 3.0)),
        ("Rumble (Earth up)", cache["rumble"], _weight(0.10, 1.30, 2.0)),
        ("Owl (an EVENT)", cache["owl"], _weight(OwlGain, 1.0, OwlMinMeters)),
        ("NightBird (an EVENT)", cache["bird"], _weight(BirdGain, 1.0, BirdMinMeters)),
    ]
    for label, d, w in rows:
        c, sp = audible(d)
        ac, lag = auto_corr(d, lo_s=0.05, hi_s=6.0)
        b = bands(d)
        dl = "       -" if w is None else f"{w20(d, 500.0) * w:8.5f}"
        print(f"  {label:<20} {flatness(d):.3f}  {sp:.2f} oct  {b[4]+b[5]:5.1f}%  "
              f"{c:6.0f} Hz  {ac:.4f} @ {lag:5.2f}s  {env_swing(d):5.2f} dB  {dl}")
    print()
    print("  THE ALTERNATIVE, FALSIFIED ON BAND AND NOT ON LEVEL. The leaves bed under a")
    print("  full Air infusion is the LOUDER of the two suspects and it also arrives and")
    print("  stays — the wind gate opens over 0.9 s and closes over 2.4 s. It is not what")
    print("  he is describing: read its >2 kHz share and its centroid against both controls.")
    print("  Rain is 2-9 kHz hiss; the wind bed is an aperture, 53.2% of it under 200 Hz.")
    print()

    print("  THE WOOD AS ONE SIGNAL — with the chorus and without it, at 4 perceived m:")
    print("  FOREST MIX           SFM   spread   >2 kHz   centroid   env AC @ lag   swing   "
          "delivered@4m")
    for label, mix in (("WITH the chorus", forest_mix(cache, chorus=True)),
                       ("WITHOUT it (the fix)", forest_mix(cache, chorus=False)),
                       ("WITH, Air full", forest_mix(cache, chorus=True, air=1.0)),
                       ("WITHOUT, Air full", forest_mix(cache, chorus=False, air=1.0))):
        c, sp = audible(mix)
        ac, lag = auto_corr(mix, lo_s=0.05, hi_s=6.0)
        b = bands(mix)
        print(f"  {label:<20} {flatness(mix):.3f}  {sp:.2f} oct  {b[4]+b[5]:5.1f}%  "
              f"{c:6.0f} Hz  {ac:.4f} @ {lag:5.2f}s  {env_swing(mix):5.2f} dB  "
              f"{w20(mix, 500.0):8.5f}")
    a = forest_mix(cache, chorus=True)
    b_ = forest_mix(cache, chorus=False)
    print(f"  => deleting it takes the wood's delivered level from {w20(a, 500.0):.5f} to "
          f"{w20(b_, 500.0):.5f} ({20*math.log10(w20(b_,500.0)/w20(a,500.0)):+.1f} dB),")
    print(f"     its 1/3-octave flatness from {flatness(a):.3f} to {flatness(b_):.3f} "
          f"(rain control {flatness(ctl_rain):.3f}, TV {flatness(ctl_tv):.3f}),")
    print(f"     and its share above 2 kHz from {sum(bands(a)[4:]):.1f}% to "
          f"{sum(bands(b_)[4:]):.1f}% (rain control {sum(bands(ctl_rain)[4:]):.1f}%).")


# =============================================================================
#  ModBuild 226 — THE FIRE'S CRACKLE STOPS BEING A WALK.
# =============================================================================
#
# USER RULING, hardware, verbatim:
#
#     "Genau wie die Easter-Eggs sollen auch die Sounds mit allen Mitspieler
#      synchronisiert sein die in der selben Map sind."
#
# EnvSound.TickFire used to write `next = now + PoissonGap(mean, draw)` and keep
# `next`, which made its phase depend on when the client started observing — the
# one cue in the feature two players did not share. It now asks, once per
# FireCrackleTickSeconds of SHARED clock, whether the site crackles in this tick
# (probability tick/mean), and places the event at a hashed offset in the tick's
# FIRST HALF.
#
# THE DEFENCE OF THAT SWAP IS THAT IT CHANGES WHO HEARS WHEN AND NOT WHAT IS
# HEARD, so this function measures it instead of asserting it. The wire vectors
# hold the same properties against a synthetic uniform stream (they cannot reach
# Haunt.Hash, which is not on that harness); this drives the REAL cascade.
FireCrackleTickSeconds = 1.31
FireGapCalm, FireGapFull = 4.0, 2.2
FireSites = 3
FireGapChannel, FireWhenChannel = 3.0, 2.0
PoissonGapMin, PoissonGapMax = 0.28, 2.60


def crackle_gaps(seconds=6 * 3600.0):
    """THE BEFORE AND AFTER GAP DISTRIBUTIONS, at one site, over six hours."""
    print("                      mean gap   min    p05    p50    p95    max    rate/room")
    for label, mean in (("fully alight", FireGapFull), ("just caught", FireGapCalm)):
        # ---- AFTER: the shipped Bernoulli tick, driven through Haunt.Hash.
        p = min(FireCrackleTickSeconds / mean, 0.90)
        times = []
        for n in range(int(seconds / FireCrackleTickSeconds)):
            key = n * FireSites            # site 0; sites 1 and 2 are key % 3 == 1, 2
            if haunt_hash(key, FireGapChannel) >= p:
                continue
            off = 0.5 * FireCrackleTickSeconds * haunt_hash(key, FireWhenChannel)
            times.append(n * FireCrackleTickSeconds + off)
        g = np.diff(np.array(times))
        print(f"  226 tick {label:<13} {g.mean():5.2f}s  {g.min():5.2f}  "
              f"{np.percentile(g,5):5.2f}  {np.percentile(g,50):5.2f}  "
              f"{np.percentile(g,95):5.2f}  {g.max():5.2f}   {FireSites/g.mean():4.2f}/s")

        # ---- BEFORE: the walk, same length, same hash, so the two columns differ only
        # in the schedule and not in the source of randomness.
        w, t, seq = [], 0.0, 0
        while t < seconds:
            t += poisson_gap(mean, haunt_hash(seq * FireSites, FireGapChannel))
            seq += 1
            w.append(t)
        gw = np.diff(np.array(w))
        print(f"  225 walk {label:<13} {gw.mean():5.2f}s  {gw.min():5.2f}  "
              f"{np.percentile(gw,5):5.2f}  {np.percentile(gw,50):5.2f}  "
              f"{np.percentile(gw,95):5.2f}  {gw.max():5.2f}   {FireSites/gw.mean():4.2f}/s")
    print()
    print("  READ THE `min` COLUMN FIRST — it is the only one that can put this cue back in the")
    print(f"  0.2-2 s band the ear reads as a RHYTHM. The tick's floor is half a tick "
          f"({0.5*FireCrackleTickSeconds:.3f} s)")
    print("  BY GEOMETRY (the latest an event can be is half a tick in, the earliest the next can")
    print(f"  be is zero into the following one), against the walk's clamp at "
          f"{PoissonGapMin} x mean.")
    print("  THE `max` COLUMN IS THE ONE PROPERTY THAT GOT WORSE and it is stated rather than")
    print(f"  buried: a geometric tail is unbounded where the walk clamped at "
          f"{PoissonGapMax} x mean. What")
    print("  bounds it in practice is that the ROOM has three sites drawing independently —")
    print("  which is not an argument, it is the next table.")
    print()

    # THE ROOM, NOT A SEAT. The ear counts every lit fire in the room, so the
    # quantity that decides whether a long tail is audible as "the fire stopped"
    # is the gap between CONSECUTIVE CRACKLES ANYWHERE, not at one site.
    print("  ALL THREE SITES TOGETHER — the gap between consecutive crackles ANYWHERE in the room:")
    print("                      mean gap   p95    max")
    for label, mean in (("fully alight", FireGapFull), ("just caught", FireGapCalm)):
        p = min(FireCrackleTickSeconds / mean, 0.90)
        room = []
        for n in range(int(seconds / FireCrackleTickSeconds)):
            for s in range(FireSites):
                key = n * FireSites + s
                if haunt_hash(key, FireGapChannel) >= p:
                    continue
                off = 0.5 * FireCrackleTickSeconds * haunt_hash(key, FireWhenChannel)
                room.append(n * FireCrackleTickSeconds + off)
        g = np.diff(np.sort(np.array(room)))
        print(f"  226 tick {label:<13} {g.mean():5.2f}s  {np.percentile(g,95):5.2f}  {g.max():5.2f}")
    print("  => the longest the ROOM is quiet is what a listener could call the feature broken,")
    print("     and it stays inside a few seconds even when the fire is barely caught. A 43 s")
    print("     silence at ONE seat is invisible while the other two are burning.")


# ---- the earlier rounds' reports, kept as the record ------------------------
def litter_control(cache):
    """THE PRIME SUSPECT, FALSIFIED. 22 ticks in a 13 s loop at ~1.7/s on a low
    wash is, physically, an idling engine — so it was measured before it was
    believed, by rendering the same buffer WITHOUT the litter."""
    for nm, d in (("MB221 marsh 22 ticks", cache["mb221marsh"]),
                  ("MB221 marsh  0 ticks", cache["mb221marsh0"])):
        b = bands(d)
        ac, lag = auto_corr(d)
        print(f"           {nm}: bands " + " ".join(f"{x:5.1f}" for x in b) +
              f"   AC {ac:.4f} @ {lag:.2f}s")
    t = slip_train(MarshTicks, MarshTickFirst, MarshTickLast,
                   MarshTickSpread, MarshTickJitter, MarshSeed)
    g = np.diff(t)
    print(f"           the train itself: mean gap {g.mean():.3f}s ({1/g.mean():.2f} Hz), "
          f"cv {g.std()/g.mean():.2f}, min {g.min():.2f} max {g.max():.2f}")
    print("           => the litter is INERT. Removing all 22 ticks moves the band split by")
    print("              0.1 pp and does not lower the autocorrelation at all. The tractor is")
    print("              not a rhythm; it is the BAND the ticks were sitting on.")


def tractor_report(cache):
    print("                       0-200 2-500 5-1k  1-2k  2-5k 5-24k | centroid/spread | "
          "delivered @4m  | 1-5k abs")
    line("MB221 Marsh", cache["mb221marsh"], 0.035)
    line("MB222 NightAir*", cache["nightair"], MB222_NightAirGain)
    line("MB221 Stone", cache["mb221stone"], 0.035)
    line("MB222 Stone*", cache["stone"], MB222_StoneGain)
    line("Candle (each)", cache["flutter"], 0.055, mod=0.86, minm=0.6)
    line("Draught (Air up)", cache["bedheard"], DraughtGain, mod=1.80, minm=DraughtMinMeters)
    line("Chirr MB221", cache["mb221chirr3k"], ChirrGainOld * ChirrLiftMB221, mod=0.90, minm=3.0)
    line("Chirr MB222", cache["chirr3k"], ChirrGainOld * MB222_ChirrLift, mod=0.90, minm=3.0)
    line("Chirr pre-221", cache["mb221chirr1150"], ChirrGainOld, mod=0.90, minm=3.0)
    line("Chirr 223 chorus", cache["chirr3k"], NightBedGain, mod=InsectChorusPeak, minm=3.0)
    print("           (* the two room tones, DELETED at ModBuild 223 — see static_report)")


def masking_report(cache):
    """WHY THE SAME BED AT THE SAME GAIN IS A TRACTOR IN ONE ROOM AND INAUDIBLE
    IN THE OTHER. Everything above 500 Hz, at 4 perceived metres, per room."""
    flut = cache["flutter"]
    c221 = cache["mb221chirr3k"]
    cnew = cache["chirr3k"]

    def d500(d, g, mod, minm):
        return delivered(d, g, mod, minm, hz=500.0)

    rows = [
        ("CELLAR MB221  stone", d500(cache["mb221stone"], 0.035, 0.93, RoomToneMinMeters),
         "one candle bed", d500(flut, 0.055, 0.86, 0.6)),
        ("CELLAR MB222  stone", d500(cache["stone"], MB222_StoneGain, 0.93, RoomToneMinMeters),
         "one candle bed", d500(flut, 0.055, 0.86, 0.6)),
        ("WOOD   MB221  marsh", d500(cache["mb221marsh"], 0.035, 0.92, RoomToneMinMeters),
         "the chirr", d500(c221, ChirrGainOld * ChirrLiftMB221, 0.90, 3.0)),
        ("WOOD   MB222 night air", d500(cache["nightair"], MB222_NightAirGain, 0.92, RoomToneMinMeters),
         "the chirr", d500(cnew, ChirrGainOld * MB222_ChirrLift, 0.90, 3.0)),
    ]
    for label, bed, other_name, other in rows:
        print(f"{label:<21} {bed:.5f}   vs {other_name:<15} {other:.5f}   "
              f"({20*math.log10(bed/other):+.1f} dB)")
    print("           MASKING IS NOT THE CELLAR'S ANSWER, and this table is what refutes it:")
    print("           a candle bed's 0.6 m rolloff minimum costs it 16.5 dB by the time the")
    print("           player is at the table, so the bed he cannot hear is 10.6 dB LOUDER than")
    print("           the one he can. What is wrong with it is the BAND — see the 200 Hz/500 Hz")
    print("           pair in tractor_report: the MB221 stone loses 5.7 dB between those two")
    print("           corners and the wood's chirr loses 0.7. The cellar was authored below the")
    print("           chain; the fix is presence (StoneHissMix), not level.")


def build_cache():
    """Every buffer, rendered once. The generators are Python loops over half a
    million samples each; rendering them per report cost this file seven minutes
    a run."""
    import candle
    import fire
    c = {}
    c["roar"] = fire.make_roar()
    c["rumble"] = make_rumble()
    c["bed"] = make_bed()
    c["bedheard"] = as_heard(c["bed"])
    c["flutter"] = candle.make_flutter()
    c["stone"] = make_stone()
    c["nightair"] = make_night_air()
    c["mb221stone"] = mb221_stone()
    c["mb221marsh"] = mb221_marsh()
    c["mb221marsh0"] = mb221_marsh(ticks=0)
    c["owl"] = make_owl()
    c["bird"] = make_bird()
    c["chirr"] = make_chirr()
    c["mb221chirr"] = mb221_chirr()
    for src, dst, hz in (("chirr", "chirr3k", InsectLowPassHz),
                         ("mb221chirr", "mb221chirr3k", InsectLowPassHz),
                         ("mb221chirr", "mb221chirr1150", 1150.0)):
        x = c[src].copy()
        low_pass(x, RATE, hz)
        c[dst] = x
    return c


if __name__ == "__main__":
    CACHE = build_cache()

    print("=== ModBuild 226: \"dieser 'Regen' Sound der ab und zu kommt und "
          "für eine Zeit bleibt\" ===")
    print("--- the schedule, first: does anything in the wood have that shape? ---")
    chorus_schedule()
    print()
    print("--- and does it sound like rain? two controls, then with/without ---")
    rain_report(CACHE)

    print()
    print("=== ModBuild 226: the fire's crackle stops being a walk — before/after ===")
    crackle_gaps()

    print()
    print("=== ModBuild 223: WAS IT TV STATIC? — 'Rauschen bei nem Fernseher' ===")
    static_report(CACHE)

    print()
    print("=== ModBuild 223: WHAT A PLAYER HEARS AT REST, after the deletion ===")
    rest_report(CACHE)

    print()
    print("=== SPECTRUM, as each source actually plays it ===")
    show(measure(CACHE["bedheard"], label="Draught"))
    show(measure(CACHE["stone"], label="Stone*"))
    show(measure(CACHE["nightair"], label="NightAir*"))
    show(measure(CACHE["owl"], label="Owl"))
    show(measure(CACHE["bird"], label="NightBird"))
    print("           (* DELETED at ModBuild 223 — kept as the before column)")

    print()
    print("=== THE PRIME SUSPECT: was the leaf litter the tractor? ===")
    litter_control(CACHE)

    print()
    print("=== WHAT THE TRACTOR ACTUALLY WAS: the insect bed's modulator ===")
    beat_report(CACHE)

    print()
    print("=== THE TRACTOR REPORT — band, spread, rhythm, level, budget ===")
    tractor_report(CACHE)

    print()
    print("=== WHY ONE ROOM AND NOT THE OTHER — everything above 500 Hz at 4 m ===")
    masking_report(CACHE)

    print()
    print("=== THE TWO CALLS — events, not beds ===")
    for nm, d, g in (("Owl", make_owl(), OwlGain), ("NightBird", make_bird(), BirdGain)):
        b = bands(d)
        print(f"{nm:<10} {len(d)/RATE:.2f}s  peak {float(np.max(np.abs(d))):.2f}  "
              f"RMS {float(np.sqrt(np.mean(d*d))):.3f}  bands " +
              " ".join(f"{x:5.1f}" for x in b))
    print("           The owl is 95.8% inside 200-500 Hz — the band the wash was evicted")
    print("           from — and that is not a contradiction: it is a near-SINE (H2 0.20,")
    print("           H3 0.06) with an onset, a glide and an end, firing about once a")
    print("           minute. The tractor was a broadband WASH that never stopped.")

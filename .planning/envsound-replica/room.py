"""THE TWO ROOM-TONE BEDS, offline — MakeStone and MakeMarsh from
src/GloomhavenVR/Core/EnvSound.Bank.cs, sample for sample, plus the CHIRR level
correction that goes with them.

WHY THIS FILE EXISTS. USER REQUEST, 2026-08-22, verbatim:

    "In Szenarios gibt es immer die dortigen Hintergrundgeräusche deswegen ist
     mir die Stille vorher nie aufgefallen. Im Keller scheint es constant
     geräusche zu geben, im Wald hingegen ist es absolut still. Ich will für
     beide eine dezente Hintergrundgeräuschkullise die zu der Umgebung passt.
     Diese soll deaktivierbar sein."

Two rooms, two beds, and one question each has to answer with a NUMBER rather
than with a claim:

  * THE CELLAR'S STONE BED must sit UNDER the candle flutter rather than beside
    it. "Under" is a spectrum statement, so it is measured as a spectrum: the
    stone's body is below the candle's 470 Hz floor and the two overlap only in
    the skirts.
  * THE WOOD'S MARSH BED must not read as WIND, because the user's standing
    ruling is that there is no wind sound without an Air infusion ("Wind
    Geräusch nur wenn auch Wind aktiv ist, sonst kein Geräusch"). That is the
    exact test candle.py had to pass for the flutter, so it is the same two
    measurements: the 0-200 Hz share and the SPREAD in octaves.
  * ...and both must be DISCREET. That is the absolute 1-5 kHz energy — the band
    the class doc protects — against the beds this file already ships.

    python3 bank.py     the validation case (Fall), then the wind bed
    python3 fire.py     the three fire layers
    python3 candle.py   candle vs wind vs roar
    python3 room.py     this: stone + marsh vs candle, draught, chirr

EVERY CONSTANT BELOW MIRRORS ONE IN EnvSoundBank's THE ROOM ITSELF (the clip
side) or EnvSound's THE ROOM ITSELF / THE INSECT FLOOR (the level side). When one
moves, move it here and re-run, or the tables in those two comments stop being
measurements and become claims.

WHY THE WOOD WAS SILENT, WHICH THIS FILE ALSO MEASURES. With no infusion up the
swamp's ONLY playing source was the Night chirr: gain 0.050 (the lowest in the
file) on a clip whose generator bands it to 2200-6500 Hz — played through the
DEFAULT runtime one-pole low pass at BedLowPassHz = 1150 Hz, which is 6.5 dB down
at 2200 and 15.3 dB down at 6500. The clip and the filter contradicted each
other, and `chirr_correction()` below prints what that cost.
"""
import math
import numpy as np
from bank import (Rng, low_pass, high_pass, normalise, loop_fade, slip_train,
                  make_bed, as_heard, measure, show, RATE, PI)

# ---- the cellar's stone bed ------------------------------------------------
# Every constant MIRRORS one in EnvSoundBank's THE ROOM ITSELF block.
StoneSeconds = 11.0
StonePink = (60.0, 240.0, 900.0)
StonePinkMix = (0.58, 0.30, 0.12)
StoneLoHz = 230.0
StoneHiHz = 520.0
StoneLoPoles = 3
StoneHiPoles = 3
StoneHissLoHz = 4000.0
StoneHissHiHz = 8000.0
StoneHissPoles = 2
StoneHissMix = 0.14
StonePeak = 0.72
StoneSeed = 0x5704E000


def make_stone(rate=RATE):
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
        high_pass(d, rate, StoneLoHz)
    for _ in range(StoneHiPoles):
        low_pass(d, rate, StoneHiHz)
    normalise(d, 1.0)

    # ---- the HISS CEILING: an INDEPENDENT white stream, band-passed well above
    # the 1-4 kHz the class doc protects, mixed in small.
    h = np.zeros(n)
    hr = Rng(StoneSeed ^ 0x48155000)
    for i in range(n):
        h[i] = hr.next()
    for _ in range(StoneHissPoles):
        high_pass(h, rate, StoneHissLoHz)
    for _ in range(StoneHissPoles):
        low_pass(h, rate, StoneHissHiHz)
    normalise(h, 1.0)
    d += h * StoneHissMix

    loop_fade(d, rate // 2)
    normalise(d, StonePeak)
    return d


# ---- the wood's marsh bed --------------------------------------------------
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

# ---- EnvSound's side: what the two beds are PLAYED at ----------------------
StoneGain = 0.035
StoneMinMeters = 5.5
MarshGain = 0.035
MarshMinMeters = 5.5


def make_marsh(rate=RATE):
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

    # ---- THE LEAF LITTER. The flutter's wick, applied to a wood: a leaf giving
    # way, a drop off a branch. Same train, same power law, and the same test —
    # a tick that stands out of the wash is an EVENT and would make the loop
    # findable; see tick_report.
    ticks = slip_train(MarshTicks, MarshTickFirst, MarshTickLast,
                       MarshTickSpread, MarshTickJitter, MarshSeed)
    tick_len = int(rate * 0.008)
    tr = Rng(MarshSeed ^ 0x7C1C0000)
    for k in range(MarshTicks):
        at = int(ticks[k] * rate)
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


# ---- the chirr, as shipped -------------------------------------------------
def make_chirr(rate=RATE):
    """EnvSoundBank.MakeChirr, unchanged — this round does not touch the clip."""
    n = rate * 7
    d = np.zeros(n)
    r = Rng(0xC317F)
    for i in range(n):
        t = i / rate
        m = (0.55
             + 0.20 * math.sin(2 * PI * 17.3 * t)
             + 0.14 * math.sin(2 * PI * 23.9 * t)
             + 0.11 * math.sin(2 * PI * 31.1 * t))
        d[i] = r.next() * m
    high_pass(d, rate, 2200.0)
    low_pass(d, rate, 6500.0)
    loop_fade(d, rate // 2)
    normalise(d, 0.55)
    return d


# ---- measurement -----------------------------------------------------------
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


def band_energy(d, gain, rate=RATE, lo=1000.0, hi=5000.0):
    """ABSOLUTE energy an emitter puts into a band: the buffer's share times the
    square of the gain EnvSound plays it at. A share is not a level."""
    s = np.abs(np.fft.rfft(d)) ** 2
    f = np.fft.rfftfreq(len(d), 1.0 / rate)
    mean = float(np.sum(s[(f >= lo) & (f < hi)]) / len(d) ** 2)
    return mean * gain * gain


def hp_level(d, gain, rate=RATE):
    """RMS through a 200 Hz high pass (the headset's own low-end rolloff, this
    project's stand-in for it) times the emitter's gain — "how loud is it"."""
    x = d.copy()
    high_pass(x, rate, 200.0)
    return float(np.sqrt(np.mean(x * x))) * gain


def tick_report(d, ticks, rate=RATE, label="leaf"):
    w = max(1, int(rate * 0.008))
    env = np.sqrt(np.convolve(d * d, np.ones(w) / w, mode="same"))
    med = float(np.median(env))
    peaks = sorted(20.0 * math.log10(float(np.max(env[max(0, int(t * rate) - 32):
                                                      int(t * rate) + int(rate * 0.012)])) / med)
                   for t in ticks)
    over5 = sum(1 for p in peaks if p > 5.0)
    crest = 20.0 * math.log10(float(np.max(np.abs(d))) / float(np.sqrt(np.mean(d * d))))
    print(f"           {label}: {len(ticks)} in {MarshSeconds:.0f} s, over the wash's own 8 ms level "
          f"by  median {peaks[len(peaks)//2]:+.1f} dB  loudest {peaks[-1]:+.1f} dB")
    print(f"                 {over5} of {len(ticks)} exceed +5 dB; buffer crest factor {crest:.1f} dB")


# ---- the chirr correction --------------------------------------------------
InsectLowPassHz = 3000.0
ChirrGainOld = 0.050
ChirrGainNew = 0.075


def chirr_correction():
    """WHAT THE WOOD'S ONE UNGATED BED WAS ACTUALLY DELIVERING, and what it
    delivers after this round. Both through the 200 Hz high pass, both times
    their own gain, so the ratio is the audible change."""
    raw = make_chirr()
    # NOT bank.as_heard(): that renormalises so two SHAPES can be compared, and
    # what this has to compare is a LEVEL. The filter is applied raw.
    o = raw.copy(); low_pass(o, RATE, 1150.0)
    nn = raw.copy(); low_pass(nn, RATE, InsectLowPassHz)
    lo = hp_level(o, ChirrGainOld)
    ln = hp_level(nn, ChirrGainNew)
    print(f"           chirr as SHIPPED  (1150 Hz one-pole, gain {ChirrGainOld:.3f}): "
          f"level {lo:.6f}")
    print(f"           chirr as CORRECTED({InsectLowPassHz:.0f} Hz one-pole, gain {ChirrGainNew:.3f}): "
          f"level {ln:.6f}   ({20*math.log10(ln/lo):+.2f} dB)")
    print(f"           1-5 kHz absolute energy: shipped {band_energy(o, ChirrGainOld):.3e}  "
          f"corrected {band_energy(nn, ChirrGainNew):.3e}")
    return o, nn


if __name__ == "__main__":
    bed = make_bed()
    draught = as_heard(bed)              # the WIND as the player is handed it
    stone = make_stone()
    marsh = make_marsh()

    # the candle flutter, for the "sit under it" test
    import candle
    flut = candle.make_flutter()

    print("=== SPECTRUM, as each source actually plays it ===")
    show(measure(draught, label="Draught"))
    show(measure(flut, label="Candle"))
    show(measure(stone, label="Stone"))
    show(measure(marsh, label="Marsh"))

    print()
    print("=== THE AUDIBLE BAND (>200 Hz) — 'a band' vs 'a rush' ===")
    for d, label in ((draught, "Draught"), (flut, "Candle"),
                     (stone, "Stone"), (marsh, "Marsh")):
        c, sp = audible(d)
        print(f"{label:<10} centroid {c:6.0f} Hz   spread {sp:.2f} octaves   "
              f"envelope 5-95% swing {env_swing(d):5.2f} dB")

    print()
    print("=== THE LEAF LITTER — event or texture? ===")
    tick_report(marsh, slip_train(MarshTicks, MarshTickFirst, MarshTickLast,
                                  MarshTickSpread, MarshTickJitter, MarshSeed))

    print()
    print("=== THE CHIRR — why the wood was silent ===")
    chirr_old, chirr_new = chirr_correction()

    print()
    print("=== DELIVERED LEVEL at a typical 4 PERCEIVED METRES ===")
    # w20hp is the loudest 20 ms RMS through a 200 Hz high pass — this project's
    # stand-in for what a Quest 3 speaker actually returns. Times the gain, times
    # the mean of the runtime modulator, times Unity's logarithmic rolloff
    # (min/d, flat inside min). That product is what reaches the ear.
    #                   buffer      gain    mean mod   minMeters
    rows = [("Draught (Air up)", draught, 0.075, 1.775, 1.2),
            ("Candle (each)",    flut,    0.055, 0.86,  0.6),
            ("Chirr SHIPPED",    chirr_old, ChirrGainOld, 0.90, 3.0),
            ("Chirr CORRECTED",  chirr_new, ChirrGainNew, 0.90, 3.0),
            ("Stone",            stone,   StoneGain, 0.93, StoneMinMeters),
            ("Marsh",            marsh,   MarshGain, 0.92, MarshMinMeters)]
    ref = None
    for label, d, g, mod, minm in rows:
        w = max(1, int(RATE * 0.020))
        hp = d.copy(); high_pass(hp, RATE, 200.0)
        w20hp = float(np.max(np.sqrt(np.convolve(hp * hp, np.ones(w) / w, mode="same"))))
        roll = min(1.0, minm / 4.0)
        lvl = w20hp * g * mod * roll
        if ref is None:
            ref = lvl
        print(f"{label:<18} gain {g:.3f}  20ms/200Hz-HP {w20hp:.3f}  rolloff {roll:.2f}  "
              f"-> {lvl:.5f}   ({20*math.log10(lvl/ref):+6.2f} dB vs the draught, "
              f"{20*math.log10(lvl/1.0):.1f} dB under a full-level game cue)")

    print()
    print("=== 1-5 kHz ABSOLUTE ENERGY — the band the class doc protects ===")
    for label, d, g, _mod, _m in rows:
        print(f"{label:<18} {band_energy(d, g):.3e}")

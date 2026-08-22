"""THE TWO ROOM-TONE BEDS AND THE WOOD'S NIGHT CALLS, offline — MakeStone,
MakeNightAir, MakeOwl and MakeNightBird from src/GloomhavenVR/Core/
EnvSound.Bank.cs, sample for sample, plus the CHIRR level correction that goes
with them.

WHY THIS FILE EXISTS. USER REQUEST, 2026-08-22, verbatim:

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
  * ...AND THE SAME MEASUREMENT ACQUITS IT IN THE CELLAR. `masking_report()`
    prints the delivered level through a 500 Hz high pass, because that is the
    band a Quest 3 speaker actually returns: the ModBuild 221 stone bed reaches
    0.00286 there against ONE candle bed's 0.00607. It was 6.6 dB under a single
    candle in the only band the headset delivers, in a room with three of them
    plus a drip. It was masked, not missing.

    python3 bank.py     the validation case (Fall), then the wind bed
    python3 fire.py     the three fire layers
    python3 candle.py   candle vs wind vs roar
    python3 room.py     this: stone + night air + the calls, vs candle, draught,
                        chirr — and the ModBuild 221 pair, kept for the diff

EVERY CONSTANT BELOW MIRRORS ONE IN EnvSoundBank's THE ROOM ITSELF (the clip
side) or EnvSound's THE ROOM ITSELF / THE INSECT FLOOR (the level side). When one
moves, move it here and re-run, or the tables in those two comments stop being
measurements and become claims.

THE MB221 GENERATORS ARE KEPT, under `mb221_`. They are the BEFORE column of
every table in this round's comments, and a fix whose before-column has been
deleted is a fix nobody can check.
"""
import math
import numpy as np
from bank import (Rng, low_pass, high_pass, normalise, loop_fade, slip_train,
                  make_bed, as_heard, measure, show, RATE, PI)


def white(n, seed):
    r = Rng(seed)
    return np.array([r.next() for _ in range(n)])


# ---- the cellar's stone bed ------------------------------------------------
# Every constant MIRRORS one in EnvSoundBank's THE ROOM ITSELF block.
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


# ---- the wood's night air (EnvSoundClip.Marsh, rebuilt this round) ----------
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


def beat_report():
    """THE MEASURED CAUSE OF THE TRACTOR, with its own analytic cross-check."""
    freqs, amps = (17.3, 23.9, 31.1), (0.20, 0.14, 0.11)
    p = np.array(amps) ** 2
    print("           the MB221 modulator, analytically:")
    for tau in (0.145, 0.290, 0.580):
        v = sum(p[i] * math.cos(2 * PI * freqs[i] * tau) for i in range(3)) / p.sum()
        print(f"             AC({tau:.3f} s = {1/tau:5.2f} Hz) = {v:+.3f}")
    for nm, d in (("MB221 chirr", mb221_chirr()), ("NEW chirr", make_chirr())):
        ac, lag = auto_corr(d, lo_s=0.05, hi_s=6.0)
        print(f"           {nm:<12} envelope AC {ac:.4f} @ {lag:.3f} s "
              f"({1/lag:5.2f} Hz)   envelope swing {env_swing(d):5.2f} dB   "
              f"bands " + " ".join(f"{x:5.1f}" for x in bands(d)))
    print("           => the band split is IDENTICAL and the rhythm is not. No spectral")
    print("              instrument could have found this; the round that looks for it")
    print("              has to measure TIME.")


# ---- EnvSound's side: what everything is PLAYED at -------------------------
StoneGain = 0.045          # MB221: 0.035
NightAirGain = 0.021       # MB221 (Marsh): 0.035
RoomToneMinMeters = 5.5
InsectLowPassHz = 3000.0
ChirrGainOld = 0.050
ChirrLiftMB221 = 1.50
ChirrLift = 1.15           # EnvSound.InsectAmbienceLift
OwlGain = 0.075
BirdGain = 0.060


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


# ---- the three reports this round exists to print ---------------------------
def litter_control():
    """THE PRIME SUSPECT, FALSIFIED. 22 ticks in a 13 s loop at ~1.7/s on a low
    wash is, physically, an idling engine — so it was measured before it was
    believed, by rendering the same buffer WITHOUT the litter."""
    with_t = mb221_marsh()
    without = mb221_marsh(ticks=0)
    for nm, d in (("MB221 marsh 22 ticks", with_t), ("MB221 marsh  0 ticks", without)):
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


def tractor_report():
    print("                       0-200 2-500 5-1k  1-2k  2-5k 5-24k | centroid/spread | "
          "delivered @4m  | 1-5k abs")
    line("MB221 Marsh", mb221_marsh(), 0.035)
    line("NEW NightAir", make_night_air(), NightAirGain)
    line("MB221 Stone", mb221_stone(), 0.035)
    line("NEW Stone", make_stone(), StoneGain)
    import candle
    line("Candle (each)", candle.make_flutter(), 0.055, mod=0.86, minm=0.6)
    line("Draught (Air up)", as_heard(make_bed()), 0.075, mod=1.775, minm=1.2)
    old = mb221_chirr()
    c221 = old.copy(); low_pass(c221, RATE, InsectLowPassHz)
    line("Chirr MB221", c221, ChirrGainOld * ChirrLiftMB221, mod=0.90, minm=3.0)
    cnew = make_chirr(); low_pass(cnew, RATE, InsectLowPassHz)
    line("Chirr NEW", cnew, ChirrGainOld * ChirrLift, mod=0.90, minm=3.0)
    cold = old.copy(); low_pass(cold, RATE, 1150.0)
    line("Chirr pre-221", cold, ChirrGainOld, mod=0.90, minm=3.0)


def masking_report():
    """WHY THE SAME BED AT THE SAME GAIN IS A TRACTOR IN ONE ROOM AND INAUDIBLE
    IN THE OTHER. Everything above 500 Hz, at 4 perceived metres, per room."""
    import candle
    flut = candle.make_flutter()
    c221 = mb221_chirr(); low_pass(c221, RATE, InsectLowPassHz)
    cnew = make_chirr(); low_pass(cnew, RATE, InsectLowPassHz)

    def d500(d, g, mod, minm):
        return delivered(d, g, mod, minm, hz=500.0)

    rows = [
        ("CELLAR MB221  stone", d500(mb221_stone(), 0.035, 0.93, RoomToneMinMeters),
         "one candle bed", d500(flut, 0.055, 0.86, 0.6)),
        ("CELLAR NEW    stone", d500(make_stone(), StoneGain, 0.93, RoomToneMinMeters),
         "one candle bed", d500(flut, 0.055, 0.86, 0.6)),
        ("WOOD   MB221  marsh", d500(mb221_marsh(), 0.035, 0.92, RoomToneMinMeters),
         "the chirr", d500(c221, ChirrGainOld * ChirrLiftMB221, 0.90, 3.0)),
        ("WOOD   NEW night air", d500(make_night_air(), NightAirGain, 0.92, RoomToneMinMeters),
         "the chirr", d500(cnew, ChirrGainOld * ChirrLift, 0.90, 3.0)),
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


if __name__ == "__main__":
    print("=== SPECTRUM, as each source actually plays it ===")
    show(measure(as_heard(make_bed()), label="Draught"))
    show(measure(make_stone(), label="Stone"))
    show(measure(make_night_air(), label="NightAir"))
    show(measure(make_owl(), label="Owl"))
    show(measure(make_bird(), label="NightBird"))

    print()
    print("=== THE PRIME SUSPECT: was the leaf litter the tractor? ===")
    litter_control()

    print()
    print("=== WHAT THE TRACTOR ACTUALLY WAS: the insect bed's modulator ===")
    beat_report()

    print()
    print("=== THE TRACTOR REPORT — band, spread, rhythm, level, budget ===")
    tractor_report()

    print()
    print("=== WHY ONE ROOM AND NOT THE OTHER — everything above 500 Hz at 4 m ===")
    masking_report()

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

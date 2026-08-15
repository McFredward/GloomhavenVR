"""OFFLINE REPLICA of src/GloomhavenVR/Core/EnvSound.Bank.cs's generators.

WHY IT IS COMMITTED. Nobody working on this feature can hear it, so every level
and every band figure in EnvSound.cs and EnvSound.Bank.cs is a number produced by
running the generators OUTSIDE Unity. ModBuild 150 built exactly such a replica
for the bookshelf's impact, used it to prove the clip was a click over a boom the
headset speaker could not make, and then did not keep it — so ModBuild 152 had to
build it again before it could put a single number on the fire. That is the whole
reason this file exists in the repo rather than in a scratch directory.

HOW TO TRUST IT. It reproduces the SHIPPED Fall clip's published table exactly —
peak 0.980 at 2.44 ms, RMS 0.0697, 90% of peak in 0.19 ms, -20 dB in 27 ms,
centroid 1530 Hz, bands 26.9/5.3/34.5/17.3/10.0/5.9. Run `python3 bank.py`; if
those numbers come out, the harness is the same arithmetic the device runs and the
figures it prints for anything else can be quoted. If they do NOT, a generator was
edited without this file being updated, and every doc table it fed is suspect.

    python3 bank.py     the validation case (Fall), then the shared wind bed
    python3 fire.py     the three fire layers (ModBuild 152)
    python3 candle.py   the candle flutter against the wind bed and the roar
                        (ModBuild 153 — the question "does this read as wind?")

The C# is the source of truth; this is a mirror, and a mirror that has drifted is
worse than no mirror. Keep the constants in step by hand, exactly as
BuildEnvironmentRooms.cs's numbers are kept in step with EnvSound.cs's.

Requires numpy. It is a measurement tool, not part of the build.
"""
import math
import numpy as np

RATE = 48000
PI = math.pi
F32 = np.float32


class Rng:
    """EnvSoundRng: xorshift32, returns -1..1."""

    def __init__(self, seed):
        self.s = seed if seed != 0 else 0x9E3779B9

    def next(self):
        s = self.s
        s ^= (s << 13) & 0xFFFFFFFF
        s ^= s >> 17
        s ^= (s << 5) & 0xFFFFFFFF
        self.s = s
        signed = s - 0x100000000 if s >= 0x80000000 else s
        return float(np.float32(signed * np.float32(4.656613e-10)))


def low_pass(d, rate, hz):
    a = min(max(1.0 - math.exp(-2.0 * PI * hz / rate), 0.0), 1.0)
    y = 0.0
    for i in range(len(d)):
        y += a * (d[i] - y)
        d[i] = y


def high_pass(d, rate, hz):
    a = min(max(1.0 - math.exp(-2.0 * PI * hz / rate), 0.0), 1.0)
    y = 0.0
    for i in range(len(d)):
        y += a * (d[i] - y)
        d[i] -= y


def normalise(d, peak):
    mx = float(np.max(np.abs(d)))
    if mx <= 1e-6:
        return
    d *= peak / mx


def soft_clip(d, drive):
    if not drive > 0.0:
        return
    normalise(d, 1.0)
    k = math.tanh(drive)
    for i in range(len(d)):
        d[i] = math.tanh(drive * d[i]) / k


def loop_fade(d, tail):
    n = len(d)
    if tail <= 0 or tail * 2 >= n:
        return
    for i in range(tail):
        t = (i + 0.5) / tail
        a = math.sin(t * PI * 0.5)
        b = math.cos(t * PI * 0.5)
        d[i] = d[i] * a + d[n - tail + i] * b
    for i in range(n - tail, n):
        d[i] = d[i - (n - tail)]


def slip_train(count, first, last, shrink, jitter, seed):
    """EnvSoundSchedule.SlipTrain."""
    into = [0.0] * count
    if count == 0:
        return into
    into[0] = first
    if count == 1:
        return into
    span = last - first
    if not span > 0.0:
        for i in range(1, count):
            into[i] = first
        return into
    r = Rng(seed)
    g = 1.0
    total = 0.0
    for i in range(1, count):
        gap = g * max(1.0 + jitter * r.next(), 0.05)
        into[i] = gap
        total += gap
        g *= shrink
    k = span / max(total, 1e-6)
    t = first
    for i in range(1, count):
        t += into[i] * k
        into[i] = min(t, last)
    into[count - 1] = last
    return into


def poisson_gap(mean, u):
    """EnvSoundSchedule.PoissonGap."""
    if not (u > 0.0 and u <= 1.0):
        u = 0.5
    m = mean if (mean > 0.0 and mean < 1e6) else 0.05
    gap = -m * math.log(u)
    return min(max(gap, m * 0.28), m * 2.60)


# ---------------------------------------------------------------- shipped Fall
FallCarcassQ = 7.0
FallModeHz = 78.0
FallModeRatio = 1.7320508
FallSlabHz = 160.0
FallSlabTau = 0.0115
FallSlapTau = 0.009
FallLoadDelay = 0.025
FallLoadTau = 0.11
FallCrackTau = 0.0016
FallCrackLoHz = 320.0
FallCrackHiHz = 5000.0
FallScatterCount = 9
FallScatterFirst = 0.035
FallScatterLast = 0.42
FallScatterSpread = 1.30
FallScatterJitter = 0.70
FallScatterTau = 0.0035
FallScatterExponent = 1.6
FallCrackMix = 2.4
FallScatterMix = 1.1
FallBoardHz = [525.0, 885.0, 1489.0, 1737.0, 2098.0]
FallBoardQ = 10.0
FallBoardRise = 0.0015
FallBoardMix = 2.5
FallBoardTilt = 0.5
FallDrive = 3.0


def make_fall(rate=RATE):
    n = int(rate * 0.9)
    d = np.zeros(n)
    h = np.zeros(n)
    r = Rng(0xFA11)
    hi = FallModeHz * FallModeRatio
    a_lo = PI * FallModeHz / FallCarcassQ
    a_hi = PI * hi / FallCarcassQ
    for i in range(n):
        t = i / rate
        s = r.next() * math.exp(-t / FallSlapTau) * 0.90 if t < 0.06 else 0.0
        if t < 0.09:
            s += math.sin(2 * PI * FallSlabHz * t) * math.exp(-t / FallSlabTau) * 0.55
        if t < 0.35:
            s += math.sin(2 * PI * FallModeHz * t) * math.exp(-a_lo * t) * 0.55
            s += math.sin(2 * PI * hi * t) * math.exp(-a_hi * t) * 0.30
        tl = t - FallLoadDelay
        if 0.0 < tl < 0.55:
            s += r.next() * (1 - math.exp(-tl * 180.0)) * math.exp(-tl / FallLoadTau) * 0.30
        d[i] = s

    hr = Rng(0xC7AC)
    crack_len = int(rate * 0.02)
    for i in range(min(crack_len, n)):
        t = i / rate
        h[i] += hr.next() * math.exp(-t / FallCrackTau) * FallCrackMix

    board_len = int(rate * 0.12)
    for f in FallBoardHz:
        a_b = PI * f / FallBoardQ
        amp = FallBoardMix * (f / FallBoardHz[0]) ** FallBoardTilt
        for i in range(min(board_len, n)):
            t = i / rate
            h[i] += math.sin(2 * PI * f * t) * (1 - math.exp(-t / FallBoardRise)) * math.exp(-a_b * t) * amp

    thrown = slip_train(FallScatterCount, FallScatterFirst, FallScatterLast,
                        FallScatterSpread, FallScatterJitter, 0xC7AC)
    tick_len = int(rate * 0.012)
    for k in range(len(thrown)):
        at = int(thrown[k] * rate)
        amp = abs(hr.next()) ** FallScatterExponent
        for i in range(tick_len):
            if at + i >= n:
                break
            tt = i / rate
            h[at + i] += hr.next() * math.exp(-tt / FallScatterTau) * amp * FallScatterMix

    high_pass(h, rate, FallCrackLoHz)
    low_pass(h, rate, FallCrackHiHz)
    low_pass(d, rate, 950.0)
    high_pass(d, rate, 55.0)
    d += h
    soft_clip(d, FallDrive)
    normalise(d, 0.98)
    return d


# --------------------------------------------------------------- the shared bed
# MakeBed. THE WIND BUFFER — the window draught and the swamp canopy ride it, and
# until ModBuild 153 the candle flames did too, which is the defect that round
# fixed. It is here rather than in candle.py because it is the REFERENCE every
# "does this read as wind?" measurement is taken against.
BedSeconds = 8.0
BedPink = (40.0, 320.0, 2600.0)
BedPinkMix = (0.62, 0.30, 0.14)
BedPeak = 0.85

# The runtime AudioLowPassFilter EnvSound.AddBed puts on every bed but the fire's
# (EnvSound.BedLowPassHz). A bed's SPECTRUM AS HEARD is the buffer through this;
# comparing raw buffers would compare something no listener is ever handed.
BedLowPassHz = 1150.0


def make_bed(rate=RATE):
    n = int(rate * BedSeconds)
    d = np.zeros(n)
    r = Rng(0x5EEDBED)
    k1 = 1 - math.exp(-2 * PI * BedPink[0] / rate)
    k2 = 1 - math.exp(-2 * PI * BedPink[1] / rate)
    k3 = 1 - math.exp(-2 * PI * BedPink[2] / rate)
    a1 = a2 = a3 = 0.0
    for i in range(n):
        w = r.next()
        a1 += k1 * (w - a1)
        a2 += k2 * (w - a2)
        a3 += k3 * (w - a3)
        d[i] = a1 * BedPinkMix[0] + a2 * BedPinkMix[1] + a3 * BedPinkMix[2]
    loop_fade(d, rate // 2)
    normalise(d, BedPeak)
    return d


def as_heard(d, rate=RATE, hz=BedLowPassHz, peak=None):
    """A bed through its runtime low pass, renormalised so the comparison is of
    SHAPE and not of level (the level is EnvSound's gain budget, not the bank's)."""
    out = d.copy()
    low_pass(out, rate, hz)
    normalise(out, peak if peak is not None else float(np.max(np.abs(d))))
    return out


# ------------------------------------------------------------------ measurement
BANDS = [(0, 200), (200, 500), (500, 1000), (1000, 2000), (2000, 5000), (5000, 24000)]


def measure(d, rate=RATE, label=""):
    n = len(d)
    peak = float(np.max(np.abs(d)))
    peak_at = int(np.argmax(np.abs(d))) / rate
    rms = float(np.sqrt(np.mean(d * d)))
    # attack: time to 90% of peak
    idx90 = int(np.argmax(np.abs(d) >= 0.9 * peak))
    attack = idx90 / rate
    # decay to -20 dB of peak, measured on a 5 ms sliding RMS after the peak
    win = max(1, int(rate * 0.005))
    env = np.sqrt(np.convolve(d * d, np.ones(win) / win, mode="same"))
    epk = float(np.max(env))
    ipk = int(np.argmax(env))
    tail = np.where(env[ipk:] < epk * 0.1)[0]
    decay20 = (tail[0] / rate) if len(tail) else float("nan")
    spec = np.abs(np.fft.rfft(d)) ** 2
    freqs = np.fft.rfftfreq(n, 1.0 / rate)
    total = float(np.sum(spec))
    bands = []
    for lo, hi in BANDS:
        m = (freqs >= lo) & (freqs < hi)
        bands.append(100.0 * float(np.sum(spec[m])) / total)
    centroid = float(np.sum(freqs * spec) / total)
    # loudest 20 ms window, and the same through a one-pole 200 Hz high pass — a
    # crude stand-in for the Quest 3's own low-end rolloff. This is what the ear
    # actually integrates an event over.
    w20 = max(1, int(rate * 0.020))
    e20 = np.sqrt(np.convolve(d * d, np.ones(w20) / w20, mode="same"))
    hp = d.copy()
    high_pass(hp, rate, 200.0)
    e20h = np.sqrt(np.convolve(hp * hp, np.ones(w20) / w20, mode="same"))
    out = dict(w20=float(np.max(e20)), w20hp=float(np.max(e20h)),
               label=label, n=n, seconds=n / rate, peak=peak, peak_at=peak_at,
               rms=rms, attack=attack, decay20=decay20, bands=bands,
               centroid=centroid,
               band1to5=100.0 * float(np.sum(spec[(freqs >= 1000) & (freqs < 5000)])) / total)
    return out


def show(m):
    print(f"{m['label']:<10} {m['seconds']:.3f}s  peak {m['peak']:.3f} @ {m['peak_at']*1000:.2f} ms  "
          f"RMS {m['rms']:.4f}  attack(90%) {m['attack']*1000:.2f} ms  -20dB {m['decay20']*1000:.1f} ms  "
          f"centroid {m['centroid']:.0f} Hz")
    print(f"           loudest 20 ms RMS {m['w20']:.4f}  through a 200 Hz high pass {m['w20hp']:.4f}")
    b = m["bands"]
    print(f"           bands  0-200 {b[0]:5.1f}%  200-500 {b[1]:5.1f}%  500-1k {b[2]:5.1f}%  "
          f"1-2k {b[3]:5.1f}%  2-5k {b[4]:5.1f}%  5-24k {b[5]:5.1f}%   [1-5k {m['band1to5']:.1f}%]")


if __name__ == "__main__":
    show(measure(make_fall(), label="Fall"))
    bed = make_bed()
    show(measure(bed, label="Bed"))
    show(measure(as_heard(bed), label="Bed@1150"))

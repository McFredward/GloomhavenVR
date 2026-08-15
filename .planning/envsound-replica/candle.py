"""THE CANDLE FLUTTER, offline — MakeFlutter from
src/GloomhavenVR/Core/EnvSound.Bank.cs, sample for sample, measured against the
two things it has to be distinguishable from: the WIND bed it used to be, and the
fire's ROAR it sits beside.

WHY THIS FILE EXISTS. USER REPORT, ModBuild 152 hardware, verbatim:

    "Beim Feuer Geräusch ist auch immer das Wind geräusch mit dabei. Das soll
     nicht sein. Das Wind gEräusch soll nur dann kommen wenn Wind auch aktiv ist."

He is right twice over, and both halves were in the source rather than in his ears:
the cellar's three "Flame<n>" candle beds played EnvSoundClip.Bed — literally the
window draught's buffer — and their gain lambda carried `+ 0.9f * ElementMood
.Live(0)`, so infusing FIRE made the WIND CLIP louder. ModBuild 153 gives the
candles their own clip and this file is the proof that the new one does not read
as the old one.

    python3 bank.py     the validation case (Fall), then the wind bed
    python3 fire.py     the three fire layers
    python3 candle.py   this: candle vs wind vs roar, spectrum AND envelope

THE COMPARISON IS TAKEN AS HEARD, not on the raw buffers. Every bed but the fire's
and the candle's runs through a runtime AudioLowPassFilter at EnvSound.BedLowPassHz
= 1150 Hz, so the draught the player is handed is not the buffer the bank made.
bank.as_heard() applies it; the candle and the roar bake their own bands and are
measured raw, which is what their sources really play.

AND THE SPECTRUM IS ONLY HALF THE TEST. Two stationary noises with different
corners can still both read as "air moving": what separates a FLAME from a
DRAUGHT is that a flame's amplitude is unsteady at a few hertz and a draught's is
not. So this file also measures the ENVELOPE — its modulation depth and where that
modulation sits in frequency — which is the number the spectral table alone would
have let us miss.
"""
import math
import numpy as np
from bank import (Rng, low_pass, high_pass, normalise, loop_fade, slip_train,
                  make_bed, as_heard, measure, show, RATE, PI)
from fire import make_roar

# ---- the candle flutter ---------------------------------------------------
# Every constant MIRRORS one in EnvSoundBank's THE CANDLE block.
FlutterSeconds = 7.0
FlutterLoHz = 470.0
FlutterHiHz = 1050.0
FlutterLoPoles = 2
FlutterHiPoles = 4
FlutterHz = 11.0
FlutterFloor = 0.38
FlutterSigmas = 1.8
FlutterTicks = 18
FlutterTickFirst = 0.08
FlutterTickLast = 6.90
FlutterTickSpread = 1.0
FlutterTickJitter = 0.90
FlutterTickTau = 0.0008
FlutterTickExponent = 1.5
FlutterTickMix = 2.0
FlutterPeak = 0.70
FlutterSeed = 0xCA9D1E00


def make_flutter(rate=RATE):
    n = int(rate * FlutterSeconds)
    d = np.zeros(n)

    # ---- pass 1: the envelope, into d, and its SIGMA (MakeRoar's two-pass shape,
    # with MakeRoar's ModBuild 153 sigma normalisation).
    e = Rng(FlutterSeed ^ 0x5A5A5A5A)
    ke = 1 - math.exp(-2 * PI * FlutterHz / rate)
    e1 = e2 = 0.0
    acc = 0.0
    for i in range(n):
        v = e.next()
        e1 += ke * (v - e1)
        e2 += ke * (e1 - e2)
        d[i] = e2
        acc += e2 * e2
    sd = math.sqrt(acc / n)
    if sd < 1e-9:
        sd = 1.0

    # ---- pass 2: white carrier times that envelope.
    r = Rng(FlutterSeed)
    span = FlutterSigmas * sd
    for i in range(n):
        u = 0.5 * (1.0 + d[i] / span)
        u = 0.0 if u < 0.0 else (1.0 if u > 1.0 else u)
        d[i] = r.next() * (FlutterFloor + (1.0 - FlutterFloor) * u)

    # ---- the wick. Tiny sputters, laid in with the same train the crackle uses.
    ticks = slip_train(FlutterTicks, FlutterTickFirst, FlutterTickLast,
                       FlutterTickSpread, FlutterTickJitter, FlutterSeed)
    tick_len = int(rate * 0.004)
    tr = Rng(FlutterSeed ^ 0x7C1C0000)
    for k in range(FlutterTicks):
        at = int(ticks[k] * rate)
        amp = abs(tr.next()) ** FlutterTickExponent * FlutterTickMix
        for i in range(tick_len):
            if at + i >= n:
                break
            tt = i / rate
            d[at + i] += tr.next() * math.exp(-tt / FlutterTickTau) * amp

    for _ in range(FlutterLoPoles):
        high_pass(d, rate, FlutterLoHz)
    for _ in range(FlutterHiPoles):
        low_pass(d, rate, FlutterHiHz)

    loop_fade(d, rate // 2)
    normalise(d, FlutterPeak)
    return d


# ---- the envelope test ----------------------------------------------------
def envelope(d, rate=RATE, win_ms=20.0):
    """The amplitude envelope the ear tracks: a sliding RMS over roughly what it
    integrates. Returned normalised to its own mean, so the numbers below are
    about SHAPE and never about level."""
    w = max(1, int(rate * win_ms / 1000.0))
    env = np.sqrt(np.convolve(d * d, np.ones(w) / w, mode="same"))
    # Drop the convolution's own edges — they taper to zero and would show up as
    # modulation that is not in the signal.
    env = env[w:-w]
    m = float(np.mean(env))
    return env / m if m > 1e-9 else env


def env_report(d, rate=RATE, label=""):
    env = envelope(d, rate)
    cv = float(np.std(env))                       # already mean-normalised
    lo, hi = float(np.percentile(env, 5)), float(np.percentile(env, 95))
    depth_db = 20.0 * math.log10(hi / max(lo, 1e-9))

    # WHERE the modulation lives. The envelope is decimated to 200 Hz first — it
    # carries nothing above 100 Hz by construction and the FFT is then cheap.
    step = max(1, rate // 200)
    e = env[::step] - 1.0
    erate = rate / step
    spec = np.abs(np.fft.rfft(e * np.hanning(len(e)))) ** 2
    f = np.fft.rfftfreq(len(e), 1.0 / erate)
    total = float(np.sum(spec[f > 0.15]))
    def share(a, b):
        m = (f >= a) & (f < b)
        return 100.0 * float(np.sum(spec[m])) / total
    centroid = float(np.sum(f[f > 0.15] * spec[f > 0.15]) / total)
    print(f"{label:<10} envelope: sigma/mean {cv:.3f}   5-95% swing {depth_db:5.2f} dB   "
          f"mod centroid {centroid:5.2f} Hz")
    print(f"           modulation  0.15-1 Hz {share(0.15,1):5.1f}%   1-3 Hz {share(1,3):5.1f}%   "
          f"3-8 Hz {share(3,8):5.1f}%   8-20 Hz {share(8,20):5.1f}%   20-100 Hz {share(20,100):5.1f}%")
    return dict(cv=cv, depth_db=depth_db, centroid=centroid)


# ---- the spectral tests ---------------------------------------------------
def audible(d, rate=RATE, lo=200.0, hi=12000.0):
    """Centroid and SPREAD (in octaves) over the band a Quest 3 speaker actually
    returns. The spread is the number that says "narrow-band": a draught is a
    broadband rush and a small flame is a band of noise, and the centroid alone
    cannot tell them apart — the draught's own centroid is dragged UP by a hiss
    tail the flame does not have."""
    s = np.abs(np.fft.rfft(d)) ** 2
    f = np.fft.rfftfreq(len(d), 1.0 / rate)
    m = (f >= lo) & (f < hi)
    s, f = s[m], f[m]
    p = s / np.sum(s)
    oct_ = np.log2(f / 1000.0)
    c = float(np.sum(oct_ * p))
    spread = float(math.sqrt(np.sum(((oct_ - c) ** 2) * p)))
    return 1000.0 * (2.0 ** c), spread


def tick_report(d, rate=RATE):
    """HOW PROMINENT THE WICK IS, which is where the loop argument is settled. Item 6
    of EnvSound's class doc forbids a recognisable EVENT inside a looping buffer, so
    the sputters are measured against the flutter's own 4 ms level: a tick that does
    not stand out of the noise is a texture, and a texture may recur."""
    w = max(1, int(rate * 0.004))
    env = np.sqrt(np.convolve(d * d, np.ones(w) / w, mode="same"))
    med = float(np.median(env))
    ticks = slip_train(FlutterTicks, FlutterTickFirst, FlutterTickLast,
                       FlutterTickSpread, FlutterTickJitter, FlutterSeed)
    peaks = sorted(20.0 * math.log10(float(np.max(env[max(0, int(t * rate) - 32):
                                                      int(t * rate) + int(rate * 0.006)])) / med)
                   for t in ticks)
    over5 = sum(1 for p in peaks if p > 5.0)
    crest = 20.0 * math.log10(float(np.max(np.abs(d))) / float(np.sqrt(np.mean(d * d))))
    print(f"           wick: {FlutterTicks} sputters in {FlutterSeconds:.0f} s, over the flutter's own "
          f"4 ms level by  median {peaks[len(peaks)//2]:+.1f} dB  loudest {peaks[-1]:+.1f} dB")
    print(f"                 {over5} of {FlutterTicks} exceed +5 dB; buffer crest factor {crest:.1f} dB "
          f"(a tick that set the peak would raise this, and Normalise sets the whole bed's level "
          f"from the peak)")


def band_energy(d, gain, rate=RATE, lo=1000.0, hi=5000.0):
    """ABSOLUTE energy an emitter puts into a band, i.e. what the "never mask"
    rule is actually about: the buffer's share of that band times the square of
    the gain EnvSound plays it at. A share is not a level."""
    s = np.abs(np.fft.rfft(d)) ** 2
    f = np.fft.rfftfreq(len(d), 1.0 / rate)
    mean = float(np.sum(s[(f >= lo) & (f < hi)]) / len(d) ** 2)
    return mean * gain * gain


if __name__ == "__main__":
    bed = make_bed()
    heard = as_heard(bed)          # the DRAUGHT as the player is handed it
    flut = make_flutter()
    roar = make_roar()

    print("=== SPECTRUM, as each source actually plays it ===")
    show(measure(heard, label="Draught"))   # Bed through the runtime 1150 Hz filter
    show(measure(flut, label="Candle"))     # no runtime filter — band baked in
    show(measure(roar, label="Roar"))       # no runtime filter — band baked in

    print()
    print("=== THE AUDIBLE BAND (>200 Hz — what the headset returns) ===")
    for d, label in ((heard, "Draught"), (flut, "Candle"), (roar, "Roar")):
        c, sp = audible(d)
        print(f"{label:<10} centroid {c:6.0f} Hz   spread {sp:.2f} octaves")

    print()
    print("=== THE WICK — is a sputter an EVENT or a TEXTURE? ===")
    tick_report(flut)

    print()
    print("=== ENVELOPE — the half a spectrum cannot see ===")
    env_report(heard, label="Draught")
    env_report(flut, label="Candle")
    env_report(roar, label="Roar")

    print()
    print("=== LEVEL ===")
    # EnvSound's own gains: Draught 0.075, Flame 0.055 (unchanged by the rebuild).
    print(f"           draught RMS {float(np.sqrt(np.mean(heard*heard))):.4f} at peak "
          f"{float(np.max(np.abs(heard))):.3f}, gain 0.075")
    print(f"           candle  RMS {float(np.sqrt(np.mean(flut*flut))):.4f} at peak "
          f"{float(np.max(np.abs(flut))):.3f}, gain 0.055")
    hpd = heard.copy(); high_pass(hpd, RATE, 200.0)
    hpf = flut.copy(); high_pass(hpf, RATE, 200.0)
    ld = float(np.sqrt(np.mean(hpd * hpd))) * 0.075
    lf = float(np.sqrt(np.mean(hpf * hpf))) * 0.055
    print(f"           through a 200 Hz high pass and times its own gain: draught {ld:.5f}, "
          f"candle {lf:.5f}  ({20*math.log10(lf/ld):+.2f} dB)")
    ed = band_energy(heard, 0.075)
    ef = band_energy(flut, 0.055)
    print(f"           ABSOLUTE 1-5 kHz energy (the 'never mask' band): draught {ed:.3e}, "
          f"candle {ef:.3e}  ({10*math.log10(ef/ed):+.2f} dB)")

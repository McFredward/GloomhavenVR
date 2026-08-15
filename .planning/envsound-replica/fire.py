"""THE FIRE'S THREE LAYERS, offline — MakeRoar, MakeCrackles and MakeEmbers from
src/GloomhavenVR/Core/EnvSound.Bank.cs, sample for sample.

Every constant below MIRRORS one in that file. Run `python3 bank.py` first: it
validates the shared harness against the shipped Fall clip's published table, and
without that check the numbers this script prints mean nothing.

The table it produces is the one quoted in EnvSound.Bank.cs's THE FIRE block.
"""
import math
import numpy as np
from bank import (Rng, low_pass, high_pass, normalise, loop_fade, slip_train,
                  measure, show, RATE, PI)

# ---- the roar -------------------------------------------------------------
RoarSeconds = 6.0
RoarPink = (90.0, 420.0, 1500.0)
RoarPinkMix = (0.46, 0.34, 0.20)
RoarPuffHz = 5.5
RoarPuffFloor = 0.42
RoarLoHz = 230.0
RoarHiHz = 820.0
RoarLoPoles = 2
RoarHiPoles = 3
RoarPeak = 0.30


def make_roar(rate=RATE, seed=0xF12E0000):
    n = int(rate * RoarSeconds)
    d = np.zeros(n)
    r = Rng(seed)
    k1 = 1 - math.exp(-2 * PI * RoarPink[0] / rate)
    k2 = 1 - math.exp(-2 * PI * RoarPink[1] / rate)
    k3 = 1 - math.exp(-2 * PI * RoarPink[2] / rate)
    a1 = a2 = a3 = 0.0
    e = Rng(seed ^ 0x5A5A5A5A)
    ke = 1 - math.exp(-2 * PI * RoarPuffHz / rate)
    e1 = e2 = 0.0
    env = np.zeros(n)
    for i in range(n):
        w = r.next()
        a1 += k1 * (w - a1)
        a2 += k2 * (w - a2)
        a3 += k3 * (w - a3)
        d[i] = a1 * RoarPinkMix[0] + a2 * RoarPinkMix[1] + a3 * RoarPinkMix[2]
        v = e.next()
        e1 += ke * (v - e1)
        e2 += ke * (e1 - e2)
        env[i] = e2
    m = float(np.max(np.abs(env)))
    if m > 1e-6:
        env = env / m
    d *= (RoarPuffFloor + (1.0 - RoarPuffFloor) * 0.5 * (1.0 + env))
    for _ in range(RoarLoPoles):
        high_pass(d, rate, RoarLoHz)
    for _ in range(RoarHiPoles):
        low_pass(d, rate, RoarHiHz)
    loop_fade(d, rate // 2)
    normalise(d, RoarPeak)
    return d


# ---- the crackle ----------------------------------------------------------
CrackleSeconds = 0.055
CracklePops = (3, 4, 5, 4)
CrackleFirst = 0.0004
CrackleLast = 0.034
CrackleSpread = 1.25
CrackleJitter = 0.75
CrackleTau = 0.00035
CrackleExponent = 1.5
CrackleFade = 0.62
CrackleLoHz = 900.0
CrackleHiHz = 3800.0
CrackleLoPoles = 2
CrackleHiPoles = 4
CracklePeaks = (0.95, 0.78, 0.95, 0.86)
CrackleSeeds = (0xC7AC1E00, 0xC7AC1E01, 0xC7AC1E02, 0xC7AC1E03)


def make_crackle(which, rate=RATE):
    n = int(rate * CrackleSeconds)
    d = np.zeros(n)
    seed = CrackleSeeds[which]
    r = Rng(seed)
    count = CracklePops[which]
    pops = slip_train(count, CrackleFirst, CrackleLast,
                      CrackleSpread, CrackleJitter, seed)
    pop_len = int(rate * 0.006)
    for k in range(count):
        at = int(pops[k] * rate)
        amp = (1.0 if k == 0 else abs(r.next()) ** CrackleExponent) * (CrackleFade ** k)
        for i in range(pop_len):
            if at + i >= n:
                break
            tt = i / rate
            d[at + i] += r.next() * math.exp(-tt / CrackleTau) * amp
    for _ in range(CrackleLoPoles):
        high_pass(d, rate, CrackleLoHz)
    for _ in range(CrackleHiPoles):
        low_pass(d, rate, CrackleHiHz)
    normalise(d, CracklePeaks[which])
    return d


# ---- the ember ------------------------------------------------------------
EmberSeconds = 0.22
EmberTicks = (4, 3)
EmberFirst = 0.001
EmberLast = 0.115
EmberSpread = 1.45
EmberJitter = 0.65
EmberTau = 0.0045
EmberExponent = 1.3
EmberFade = 0.70
EmberRingHz = (620.0, 980.0)
EmberRingQ = 6.0
EmberRingMix = 0.45
EmberLoHz = 500.0
EmberHiHz = 2600.0
EmberLoPoles = 2
EmberHiPoles = 2
EmberPeak = 0.55
EmberSeeds = (0xE0BE0000, 0xE0BE0001)


def make_ember(which, rate=RATE):
    n = int(rate * EmberSeconds)
    d = np.zeros(n)
    seed = EmberSeeds[which]
    r = Rng(seed)
    count = EmberTicks[which]
    ticks = slip_train(count, EmberFirst, EmberLast,
                       EmberSpread, EmberJitter, seed)
    tick_len = int(rate * 0.05)
    for k in range(count):
        at = int(ticks[k] * rate)
        amp = (1.0 if k == 0 else abs(r.next()) ** EmberExponent) * (EmberFade ** k)
        f = EmberRingHz[k % len(EmberRingHz)]
        a = PI * f / EmberRingQ
        for i in range(tick_len):
            if at + i >= n:
                break
            tt = i / rate
            d[at + i] += (r.next() * math.exp(-tt / EmberTau)
                          + math.sin(2 * PI * f * tt) * math.exp(-a * tt) * EmberRingMix) * amp
    for _ in range(EmberLoPoles):
        high_pass(d, rate, EmberLoHz)
    for _ in range(EmberHiPoles):
        low_pass(d, rate, EmberHiHz)
    normalise(d, EmberPeak)
    return d


if __name__ == "__main__":
    show(measure(make_roar(), label="Roar"))
    for i in range(4):
        show(measure(make_crackle(i), label=f"Crackle{i}"))
    for i in range(2):
        show(measure(make_ember(i), label=f"Ember{i}"))

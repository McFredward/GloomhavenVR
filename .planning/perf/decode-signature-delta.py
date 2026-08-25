import re
P = 1099511628211
M = 1 << 64
BITS = {1: 'Mesh', 2: 'Particles', 4: 'Mountable', 8: 'Mod',
        16: 'WallFade', 32: 'Foliage', 64: 'Water', 128: 'Active'}
inv = pow(P, -1, M)
path = '/tmp/claude-1000/-home-claw-gloomhaven-vr/2eeab786-170b-459a-a52d-6d9fe0e9c71c/scratchpad/sigs.txt'
for line in open(path):
    m = re.match(r'banked ([0-9A-F]+)/([0-9A-F]+), live ([0-9A-F]+)/([0-9A-F]+)', line.strip())
    if not m:
        continue
    bs, bx, ls, lx = [int(g, 16) for g in m.groups()]
    d = (ls - bs) % M
    k = (d * inv) % M
    ks = k if k < M // 2 else k - M
    tag = ''
    if abs(ks) < 1000000:
        a = abs(ks)
        tag = f"   k={ks} * P"
        if a in BITS:
            tag += f"  => ONE renderer, bit {BITS[a]} {'SET' if ks > 0 else 'CLEARED'}"
        elif a < 256:
            tag += f"  => one renderer, bits {[n for b, n in BITS.items() if a & b]}"
        else:
            tag += f"  => 0x{a:X}; /128 = {a/128:.3f}"
    else:
        tag = "   (not a small multiple of P -> renderer set changed, not just bits)"
    print(f"sumDelta={d:016X}{tag}")

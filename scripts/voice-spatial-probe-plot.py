#!/usr/bin/env python3
"""GloomhavenVR — VoiceSpatialProbe: plot a probe CSV so the CONTROLS are visible in the picture.

    voice-spatial-probe-plot.py <in.csv> <out.png>

Three panels, and the reason there are three:

  1. balance vs bearing, polar     — the shape of the pan. A null control that reads flat here is
                                     the whole reason to trust the shape beside it.
  2. balance vs bearing, cartesian — the same numbers on an axis you can read a value off. The
                                     polar plot is legible; this one is quotable.
  3. level_db vs perceived metres  — the rolloff. Log x, because a rolloff is a ratio law.

A case named/behaving as the silence control is NOT drawn as a peer series: it is the noise floor,
so it is drawn once as a neutral reference band across both level panels and as a stated number.
Drawing it as a fifth colour would say it is another curve to compare, and it is not — it is the
zero the other curves are measured against.
"""
import csv
import math
import sys
from collections import OrderedDict

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
from matplotlib.transforms import ScaledTranslation

# ---- palette -------------------------------------------------------------------------------
# Four categorical slots, validated all-pairs on the light surface
# (validate_palette.js "#2a78d6,#eb6834,#1baf7a,#4a3aa7" --mode light --pairs all -> ALL CHECKS
# PASS; worst CVD dE 9.2, worst normal-vision dE 16.3). Aqua sits below 3:1 on this surface, so
# the relief rule applies and every series carries a direct label as well as the legend.
SURFACE = "#fcfcfb"
INK = "#0b0b0b"
INK2 = "#52514e"
MUTED = "#8b8a84"
GRID = "#e6e5e0"
SERIES = ["#2a78d6", "#eb6834", "#1baf7a", "#4a3aa7"]
STYLES = ["-", (0, (5, 2)), (0, (1.6, 1.8)), (0, (7, 2, 1.6, 2))]
MARKERS = ["o", "s", "^", "D"]
FLOOR_COLOR = "#9a9992"


def read(path):
    rows = []
    with open(path, newline="") as fh:
        for r in csv.DictReader(fh):
            rows.append({
                "case": r["case"],
                "mode": r["mode"],
                "bearing": float(r["bearing_deg"]),
                "dist": float(r["distance_m_perceived"]),
                "world": float(r["distance_world"]),
                "rms_l": float(r["rms_l"]),
                "rms_r": float(r["rms_r"]),
                "rms_t": float(r["rms_total"]),
                "db": float(r["level_db"]),
                "bal": float(r["balance"]),
            })
    return rows


def place_labels(ax, items):
    """Direct labels at the series' last point, nudged apart so overlapping curves stay readable.

    Two cases that land on the same value — a null control and a hard pan both pinned at their own
    constant — would otherwise print on top of each other and the picture would show three labels
    for four series. The nudge is vertical only and never moves a label off its own curve's end.
    """
    if not items:
        return
    lo, hi = ax.get_ylim()
    gap = abs(hi - lo) * 0.055
    placed = sorted(items, key=lambda t: t[1])
    ys = []
    for _, y, _ in placed:
        if ys and y - ys[-1] < gap:
            y = ys[-1] + gap
        ys.append(y)
    offset = ScaledTranslation(8 / 72.0, 0, ax.figure.dpi_scale_trans)
    for (x, _, text), y in zip(placed, ys):
        ax.text(x, y, text, transform=ax.transData + offset, fontsize=8, color=INK2,
                va="center", ha="left", clip_on=False)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    csv_path, png_path = sys.argv[1], sys.argv[2]
    # Optional third argument: a provenance note the caller has VERIFIED, e.g. which capture
    # backend actually produced this CSV. The plot never guesses it — a picture that names a
    # backend it cannot see is the same failure as a curve that was not measured.
    note = sys.argv[3] if len(sys.argv) > 3 else ""
    rows = read(csv_path)
    if not rows:
        print("PLOT: the CSV has no rows — nothing to draw.", file=sys.stderr)
        return 1

    cases = list(OrderedDict((r["case"], None) for r in rows))

    # The silence control identifies itself: every one of its rows is at the floor. Detected, not
    # assumed from the name, so a config that renames it still gets the reference treatment.
    peak_by_case = {c: max(r["rms_t"] for r in rows if r["case"] == c) for c in cases}
    loudest = max(peak_by_case.values()) if peak_by_case else 0.0
    floor_cases = [c for c in cases
                   if loudest > 0 and peak_by_case[c] <= loudest * 1e-3] or []
    series_cases = [c for c in cases if c not in floor_cases]

    floor_rows = [r for r in rows if r["case"] in floor_cases]
    floor_db = max((r["db"] for r in floor_rows), default=None)
    floor_rms = max((r["rms_t"] for r in floor_rows), default=None)

    fig = plt.figure(figsize=(15.5, 5.6), dpi=140, facecolor=SURFACE)
    gs = fig.add_gridspec(1, 3, width_ratios=[1.0, 1.25, 1.25], wspace=0.30,
                          left=0.045, right=0.915, top=0.775, bottom=0.20)
    ax_polar = fig.add_subplot(gs[0, 0], projection="polar")
    ax_bal = fig.add_subplot(gs[0, 1])
    ax_lvl = fig.add_subplot(gs[0, 2])
    for ax in (ax_bal, ax_lvl):
        ax.set_facecolor(SURFACE)
        for side in ("top", "right"):
            ax.spines[side].set_visible(False)
        for side in ("left", "bottom"):
            ax.spines[side].set_color(GRID)
        ax.tick_params(colors=INK2, labelsize=8)
        ax.grid(True, color=GRID, linewidth=0.8)
        ax.set_axisbelow(True)
    ax_polar.set_facecolor(SURFACE)
    ax_polar.grid(True, color=GRID, linewidth=0.8)
    ax_polar.tick_params(colors=INK2, labelsize=7.5)

    bal_labels, lvl_labels = [], []

    # ---- 1 & 2: balance vs bearing -----------------------------------------------------------
    ax_polar.set_theta_zero_location("N")
    ax_polar.set_theta_direction(-1)            # clockwise: matches the probe's bearing convention
    ax_polar.set_rlim(-1.15, 1.15)
    ax_polar.set_rticks([-1, -0.5, 0, 0.5, 1])
    ax_polar.set_yticklabels(["-1", "", "0", "", "+1"], fontsize=7, color=MUTED)
    ax_polar.set_xticks([math.radians(a) for a in range(0, 360, 45)])
    ax_polar.set_xticklabels(["0°\nahead", "45°", "90°\nright", "135°", "180°\nbehind",
                              "225°", "270°\nleft", "315°"], fontsize=7.5, color=INK2)

    for i, c in enumerate(series_cases):
        pan = sorted([r for r in rows if r["case"] == c and r["mode"] == "pan"],
                     key=lambda r: r["bearing"])
        if not pan:
            continue
        col = SERIES[i % len(SERIES)]
        sty = STYLES[i % len(STYLES)]
        mk = MARKERS[i % len(MARKERS)]
        th = [math.radians(r["bearing"]) for r in pan] + [math.radians(pan[0]["bearing"])]
        bl = [r["bal"] for r in pan] + [pan[0]["bal"]]
        ax_polar.plot(th, bl, color=col, linewidth=2.0, linestyle=sty,
                      zorder=3 + i)
        ax_bal.plot([r["bearing"] for r in pan], [r["bal"] for r in pan], color=col,
                    linewidth=2.0, linestyle=sty,
                    marker=mk, markersize=4.2, markeredgecolor=SURFACE, markeredgewidth=0.9,
                    zorder=3 + i, label=c)
        # direct label at the right edge (the relief rule: identity is never colour alone)
        bal_labels.append((pan[-1]["bearing"], pan[-1]["bal"], c))

    ax_bal.axhline(0, color=MUTED, linewidth=1.0, zorder=1)
    ax_bal.set_xlim(-8, 400)
    ax_bal.set_ylim(-1.18, 1.18)
    ax_bal.set_xticks(range(0, 361, 45))
    ax_bal.set_xlabel("bearing, degrees clockwise from ahead", fontsize=9, color=INK2)
    ax_bal.set_ylabel("balance   (R−L)/(R+L)", fontsize=9, color=INK2)
    place_labels(ax_bal, bal_labels)
    ax_bal.set_title("Balance vs bearing", fontsize=10.5, color=INK, loc="left", pad=8)
    ax_polar.set_title("Balance, polar", fontsize=10.5, color=INK, loc="left", pad=9)

    # ---- 3: level vs distance ----------------------------------------------------------------
    for i, c in enumerate(series_cases):
        rol = sorted([r for r in rows if r["case"] == c and r["mode"] == "rolloff"],
                     key=lambda r: r["dist"])
        if not rol:
            continue
        col = SERIES[i % len(SERIES)]
        sty = STYLES[i % len(STYLES)]
        mk = MARKERS[i % len(MARKERS)]
        ax_lvl.plot([r["dist"] for r in rol], [r["db"] for r in rol], color=col, linewidth=2.0,
                    linestyle=sty,
                    marker=mk, markersize=4.2, markeredgecolor=SURFACE, markeredgewidth=0.9,
                    zorder=3 + i, label=c)
        lvl_labels.append((rol[-1]["dist"], rol[-1]["db"], c))

    ax_lvl.set_xscale("log")
    ax_lvl.set_xlabel("distance, PERCEIVED metres (world = × rigScale)", fontsize=9, color=INK2)
    ax_lvl.set_ylabel("level, dB re full scale", fontsize=9, color=INK2)
    ax_lvl.set_title("Level vs distance", fontsize=10.5, color=INK, loc="left", pad=8)

    place_labels(ax_lvl, lvl_labels)

    # ---- the noise floor, as a reference and not as a series ---------------------------------
    if floor_db is not None:
        for ax in (ax_lvl,):
            lo = ax.get_ylim()[0]
            ax.axhspan(min(lo, floor_db - 6), floor_db, color=FLOOR_COLOR, alpha=0.16, zorder=0)
            ax.axhline(floor_db, color=FLOOR_COLOR, linewidth=1.2, linestyle=(0, (2, 2)), zorder=2)
            ax.annotate(f"noise floor  {floor_db:.1f} dB  ({', '.join(floor_cases)})",
                        xy=(ax.get_xlim()[0], floor_db), xytext=(4, 4),
                        textcoords="offset points", fontsize=7.5, color=INK2)

    # ---- legend + the headline control readings ----------------------------------------------
    handles = [Line2D([0], [0], color=SERIES[i % len(SERIES)], linewidth=2.0,
                      linestyle=STYLES[i % len(STYLES)],
                      marker=MARKERS[i % len(MARKERS)], markersize=4.2, label=c)
               for i, c in enumerate(series_cases)]
    if floor_cases:
        handles.append(Line2D([0], [0], color=FLOOR_COLOR, linewidth=1.2, linestyle=(0, (2, 2)),
                              label="noise floor (" + ", ".join(floor_cases) + ")"))
    fig.legend(handles=handles, loc="lower center", ncol=min(5, len(handles)), frameon=False,
               fontsize=8.5, labelcolor=INK2, bbox_to_anchor=(0.5, 0.055))

    # A one-line verdict, computed from the same rows that are drawn. It is written here rather
    # than left to the reader because "the null control is flat" is the claim the rest of the
    # picture depends on, and a picture that does not state it invites the reader to assume it.
    bits = []
    for c in cases:
        pan = [r for r in rows if r["case"] == c and r["mode"] == "pan"]
        if not pan:
            continue
        bals = [r["bal"] for r in pan]
        bits.append(f"{c}: balance {min(bals):+.3f}…{max(bals):+.3f}")
    if floor_rms is not None:
        bits.append(f"noise floor rms {floor_rms:.3e}")
    fig.suptitle("Unity spatialiser, measured from the final mix",
                 fontsize=13, color=INK, x=0.045, ha="left", y=0.972)
    if note:
        fig.text(0.045, 0.928, note, fontsize=8.5, color=INK2, ha="left")
    fig.text(0.045, 0.885, "   ·   ".join(bits), fontsize=8, color=INK2, ha="left")
    fig.text(0.045, 0.018, csv_path, fontsize=7, color=MUTED, ha="left")

    fig.savefig(png_path, facecolor=SURFACE)
    print(f"PLOT wrote {png_path} ({len(rows)} rows, {len(cases)} cases)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""ROUND 3's SHIPPED CAPS AGAINST ROUND 4's — both rendered ON their own boards.

    python3 unity/board-prep/buttons/cap_sheet_beforeafter.py [-o path.png]

WHY BOTH HALVES ARE RENDERED THE SAME WAY, and why that is the only honest option.

The "before" column is not a screenshot and not an old file: it is ModBuild 289's cap, built by
the SAME `CardMesh` in the same run (`ExportCapMeshes` calls it with
`CapFaceLayout.PlainConstruction`), placed in the same seats, lit by the same two baked studio
directions, at the same camera — and sampling the same, NEW, atlas, because the atlas is written
into the bundle and there is only one of it. So exactly one variable differs between the columns:
the CONSTRUCTION. The sheet says so rather than claiming the atlas too, which would have credited
this comparison with a change it does not show.

A before/after built from a stored old render would have carried every incidental difference of
whatever run produced it — a different resolution, a different framing, a different atlas revision
— and every one of those would have been read as part of the improvement.
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cap_options as CO                                              # noqa: E402
import cap_sheet_options as SO                                        # noqa: E402

ONBOARD = os.path.join(CO.DEBUG, "onboard")
BG, FG, DIM, ACC = (26, 26, 28), (238, 238, 232), (150, 150, 146), (232, 196, 96)

GAINED = {
    "oak": "clipped joinery corners · the board's own DENTIL RING as raised blocks · "
           "square-edged land · undercut skirt",
    "steel": "filleted corners · stepped terrace bezel · four DOME RIVETS · undercut skirt",
    "bronze": "deep fillets · stepped terrace bezel · four DOME BOSSES · undercut skirt",
}


def build(dst, tile=760, pad=26):
    rows = []
    for s in CO.STYLES:
        b = os.path.join(ONBOARD, f"{s}_before_close.png")
        a = os.path.join(ONBOARD, f"{s}_after_close.png")
        w = os.path.join(ONBOARD, f"{s}_after.png")
        for p in (b, a, w):
            if not os.path.isfile(p):
                raise SystemExit(f"missing {p} — render with cap_onboard.py first")
        rows.append((s, b, a, w))

    head, cap = 72, 40
    row_h = head + tile + cap + pad
    W = pad * 2 + tile * 3 + pad * 2
    H = 108 + row_h * 3
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)
    f_t, f_s, f_c, f_n = (SO._font(40, True), SO._font(34, True),
                          SO._font(26, True), SO._font(21))

    d.text((pad, 26), "Board buttons — ModBuild 289 against round 4, both ON their own board",
           font=f_t, fill=FG)
    # BOTH COLUMNS WEAR ROUND 4's ATLAS, and captioning this "construction and atlas" would
    # overstate it. The atlas is written into the bundle, so a "before" cap rendered now
    # necessarily samples the new one. Only the CONSTRUCTION differs across the pair — a weaker
    # claim than a before/after usually implies, and the true one. It also makes the comparison
    # cleaner rather than dirtier: exactly one variable moves.
    d.text((pad, 72), "Same mesh builder, same seats, same light, same camera — and BOTH columns "
                      "wear round 4's atlas. The only difference shown is the CONSTRUCTION.",
           font=f_n, fill=DIM)

    y = 112
    for s, b, a, w in rows:
        d.text((pad, y + 4), s.upper(), font=f_s, fill=FG)
        d.text((pad + 190, y + 14), GAINED[s], font=f_n, fill=ACC)
        yy = y + head
        for i, (path, label) in enumerate(((b, "BEFORE — ModBuild 289 construction"),
                                           (a, "AFTER — round 4"),
                                           (w, "AFTER — the whole board"))):
            x = pad + i * (tile + pad)
            with Image.open(path) as p:
                p = p.convert("RGB").resize((tile, tile), Image.LANCZOS)
            im.paste(p, (x, yy))
            d.rectangle([x - 1, yy - 1, x + tile, yy + tile],
                        outline=ACC if i == 1 else (70, 70, 72), width=3 if i == 1 else 1)
            d.text((x + 4, yy + tile + 8), label, font=f_c,
                   fill=ACC if i else DIM)
        y += row_h

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    im.save(dst)
    print(f"[beforeafter] {dst}  {im.size[0]}x{im.size[1]}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-o", "--out", default=os.path.join(CO.DEBUG, "before_after_on_boards.png"))
    build(ap.parse_args().out)


if __name__ == "__main__":
    main()

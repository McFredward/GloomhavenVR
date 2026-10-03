#!/usr/bin/env python3
"""Bind the real public cabinet claim/visibility bridge into the mirror fixture.

The caller supplies Publisher.cs's existing catalogue boundary. The claim,
visibility commit and drawer follower are production methods, not test models.
Catalogue construction/row ownership, unlocks and map state remain explicit
native-game boundaries in PublicMerchantBoundary.cs.
"""
import hashlib
from pathlib import Path


def block(text, signature):
    start = text.index("    " + signature)
    opening = text.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def sources(root):
    base = Path(root) / "src/GloomhavenVR/WorldUI/TownServices"
    merchant = (base / "TownServicePublicMerchant.cs").read_text()
    drawer = (base / "TownServiceMerchantDrawer.cs").read_text()
    claim = "\n".join(block(merchant, signature) for signature in (
        "internal static bool CanClaim", "internal static void Claim()",
        "private static void CommitPublicVisibility()"))
    follower = block(drawer, "internal void Follow(TownRackState state)")
    generated = ("using UnityEngine; using GloomhavenVR.Net.TownServices; "
        "using GloomhavenVR.WorldUI.MapRoom; namespace GloomhavenVR.WorldUI { "
        "internal static partial class TownServicePublicMerchant {\n" + claim
        + "\n} internal sealed partial class TownServiceMerchantDrawer {\n"
        + follower + "\n} }\n")
    return {"PublicMerchantClaim.cs": generated}, {
        "TownServicePublicMerchant.cs": hashlib.sha256(merchant.encode()).hexdigest(),
        "TownServiceMerchantDrawer.cs": hashlib.sha256(drawer.encode()).hexdigest()}

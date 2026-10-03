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
    category = (base / "TownServiceCatalogCategory.cs").read_text()
    claim = "\n".join(block(merchant, signature) for signature in (
        "internal static bool CanClaim", "internal static void Claim()", "private static void FollowPublicRack()",
        "internal static bool TrySelectCategory(TownServiceMerchantDrawer rack, int category)", "private static void TakePublicCatalog()",
        "private static void CommitPublicVisibility()"))
    follower = "\n".join(block(drawer, signature) for signature in (
        "internal void Follow(TownRackState state)", "internal bool Select(int category, bool selling)",
        "private bool Begin(int page, int direction = 0)", "internal void Tick(float opacity)"))
    retain_start = drawer.index("    internal bool RetainsPage(int page)")
    follower += drawer[retain_start:drawer.index(";", retain_start)+1]
    generated = ("using System; using UnityEngine; using GloomhavenVR.Hands; using GloomhavenVR.Net.TownServices; "
        "using GloomhavenVR.WorldUI.MapRoom; namespace GloomhavenVR.WorldUI { "
        "internal static partial class TownServicePublicMerchant {\n" + claim
        + "\n} internal sealed partial class TownServiceMerchantDrawer {\n"
        + follower + "\n} internal sealed partial class TownServiceCatalogCategory {\n"
        + block(category, "public void OnPoke(VRHand hand)") + "\n"
        + block(category, "internal void Tick(float opacity)") + "\n} }\n")
    return {"PublicMerchantClaim.cs": generated}, {
        "TownServicePublicMerchant.cs": hashlib.sha256(merchant.encode()).hexdigest(),
        "TownServiceMerchantDrawer.cs": hashlib.sha256(drawer.encode()).hexdigest(),
        "TownServiceCatalogCategory.cs": hashlib.sha256(category.encode()).hexdigest()}

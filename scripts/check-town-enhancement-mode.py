#!/usr/bin/env python3
"""Exercise exact native tab/mode/card/area methods with complete quiet preparation.

Native source is read-only. No native payment, save, texture or network service is
run. The original selection/filter/highlight bodies and Unity Toggle behavior are
executed, rather than unconditional callback counters masking initialization.
"""
import argparse
import hashlib
import importlib.util
from pathlib import Path
import re
import sys

SCRIPT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("quiet_mode_runner", SCRIPT / "check-town-quiet-controller.py")
quiet = importlib.util.module_from_spec(spec)
spec.loader.exec_module(quiet)
quiet_sources = quiet.sources
native_source_root = None


def section(source, signature):
    match = re.search(r"(?m)^\t" + re.escape(signature), source)
    if match is None:
        raise RuntimeError("Native method binding drift: " + signature)
    end = source.index("\n\t}", match.start()) + len("\n\t}")
    return source[match.start():end]


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError("Fixture boundary drift: " + before)
    return source.replace(before, after, 1)


def sources(root):
    bound, hashes = quiet_sources(root)
    native_root = (native_source_root or root / "decompiled") / "GH.Runtime"
    native = {name: (native_root / name).read_text() for name in (
        "UINewEnhancementWindow.cs", "UnityEngine.UI/UITab.cs", "EnhancementUtils.cs", "EnhancementLineFilter.cs")}
    boundaries = (root / "scripts/town-quiet-controller-runtime/Boundaries.cs").read_text()
    hashes["QuietFixtureBoundaries.cs"] = hashlib.sha256(boundaries.encode()).hexdigest()
    for name in ("AbilityCardUI", "NativeEnhancementShop", "UIPartyCharacterEnhancementAbilityCardsDisplay", "UINewEnhancementWindow"):
        boundaries = replace_once(boundaries, "public " + ("sealed " if name == "NativeEnhancementShop" else "") + "class " + name,
                                  "public " + ("sealed " if name == "NativeEnhancementShop" else "") + "partial class " + name)
    boundaries = replace_once(boundaries, "public sealed class MapPartyEnhancementShopService", "public sealed partial class MapPartyEnhancementShopService")
    tab_start = boundaries.index("public sealed class NativeBuyTab {")
    tab_end = boundaries.index("\n", tab_start)
    boundaries = boundaries[:tab_start] + boundaries[tab_end:]
    for name in ("buyButton", "sellButton"):
        boundaries = replace_once(boundaries, "public NativeBuyTab " + name + "=new();", "public NativeBuyTab " + name + "=null!;")
    boundaries = replace_once(boundaries, "public bool BuyMode;", "public bool BuyMode=>mode==ShopMode.BUY;")
    for method in (
        "    public void ShowBuyOptions(){BuyMode=true;if(lastShowedCard!=null)OnSelectedCardToEnhance(lastShowedCard);}\n",
        "    public void OnSelectedCardToEnhance(AbilityCardUI? card){Selections++;selectedCard=card;previousSelectedCard=card;}\n"):
        boundaries = replace_once(boundaries, method, "")
    bound["FixtureBoundaries.cs"] = boundaries
    shop = native["UINewEnhancementWindow.cs"]
    methods = [section(shop, signature) for signature in (
        "private enum ShopMode", "private void ShowBuyOptions()", "private void ShowCard(",
        "private void OnSelectedCardToEnhance(", "private void SwitchToCard(",
        "private void SetEnhanceFilters(", "private void HighlightButtons(", "private void ClearHighlights(",
        "private void OnSelectedEnhacementButton(", "private IEnumerable<EnhancementSlot> GetSellSlots(",
        "private List<EnhancementSlot> GetBuySlots(")]
    # Only visibility changes; the original public metadata reference allows the
    # same method to be called by the mod. The decompiler reports it private.
    methods[3] = methods[3].replace("private void OnSelectedCardToEnhance", "public void OnSelectedCardToEnhance", 1)
    callback = shop[shop.index("\t\tbuyButton.onValueChanged.AddListener("):shop.index("\t\tsellButton.onValueChanged.AddListener(")]
    generated = "#nullable disable\nusing System;using System.Collections.Generic;using System.Linq;using UnityEngine;using ScenarioRuleLibrary;\npublic partial class UINewEnhancementWindow {\n" + "\n".join(methods) + "\nprivate void InstallOriginalTabCallback(){\n" + callback + "}\n}\n"
    tab_method = section(native["UnityEngine.UI/UITab.cs"], "public void Activate()")
    # An entry counter observes invocation; it does not manufacture a Toggle event.
    tab_method = tab_method.replace("\n\t{", "\n\t{\n\t\tActivations++;", 1)
    generated += "public sealed class NativeBuyTab : UnityEngine.UI.Toggle { public int Activations;\n" + tab_method + "\n}\n"
    utility_methods = [section(native["EnhancementUtils.cs"], signature) for signature in (
        "public static List<EnhancementLine> GetEnhancementLines(",
        "private static IEnumerable<EnhancementButtonBase> GetEnhancementButtons(",
        "public static bool CanBeEnhanced(AbilityCardUI", "public static bool HaveEnhancementButtons(",
        "public static bool CanBeEnhanced(this EnhancementButtonBase")]
    generated += "public static class EnhancementUtils {\n" + "\n".join(utility_methods) + "\n}\n"
    generated += native["EnhancementLineFilter.cs"]
    # A file-scoped using after declarations is invalid; preserve the exact class.
    generated = generated[:generated.rindex("using System;")] + native["EnhancementLineFilter.cs"][native["EnhancementLineFilter.cs"].index("public class EnhancementLineFilter"):]
    bound["NativeEnhancementMode.cs"] = generated
    for name, value in native.items():
        hashes["native/" + name] = hashlib.sha256(value.encode()).hexdigest()
    hashes["NativeEnhancementMode.bound.cs"] = hashlib.sha256(generated.encode()).hexdigest()
    hashes["FixtureBoundaries.bound.cs"] = hashlib.sha256(boundaries.encode()).hexdigest()
    for path in (SCRIPT / "town-enhancement-mode-runtime").glob("*"):
        if path.is_file():
            hashes["fixture/" + path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    return bound, hashes


def mutations():
    return [
        ("already-on-mode-regression", "TownServiceQuietController.cs",
         '                if (shop.buyButton.isOn) Invoke(shop, "ShowBuyOptions");\n                else shop.buyButton.Activate();',
         "                shop.buyButton.Activate();", "enchantable owned native card exposes every original selectable area immediately"),
        ("missing-off-tab-activation", "TownServiceQuietController.cs", "                else shop.buyButton.Activate();",
         "                else { }", "enchantable owned native card exposes every original selectable area immediately"),
        ("original-area-filter-regression", "NativeEnhancementMode.cs", "mode == ShopMode.BUY) ||", "mode == ShopMode.SELL) ||",
         "enchantable owned native card exposes every original selectable area immediately"),
    ]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--native-source-root", type=Path)
    native_args, remaining = parser.parse_known_args()
    native_source_root = native_args.native_source_root
    sys.argv[1:] = remaining
    quiet.sources = sources
    quiet.mutations = mutations
    quiet.main(SCRIPT / "town-enhancement-mode-runtime")

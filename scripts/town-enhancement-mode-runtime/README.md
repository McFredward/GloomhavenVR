# Native enchantress mode initialization fixture

`check-town-enhancement-mode.py` binds the entire production quiet controller and
source-window mask. It also extracts the read-only game's original `UITab.Activate`,
buy-tab callback, shop mode enum, card selection, filtering, option routing and
highlight methods. The method bodies are unchanged; one access modifier permits
the mod's existing public metadata call, and a tab-entry counter observes calls.
Source and generated binding SHA-256 hashes accompany every run.

Real Unity `Toggle.isOn` invokes its event only after a value change. Tests cover
already-on tabs with initial NONE and retained SELL mode, off-tab entry, existing
BUY mode, partly/full/not-enhanceable cards, area selection, cancel/replacement,
native proxy refresh and exact source transitions through merchant and logical
temple contexts. The original temple controller is an explicit boundary, not an
implementation simulated by these tests. Three causal controls reproduce the
missing already-on branch, missing off-tab activation and wrong native area mode.

Card pooling/art, enhancement prices/payments and model services remain explicit
boundaries. This proves native initialization and original area/option routing;
it does not prove headset rendering, game save changes, full donation lifecycle
or multiplayer latency. The existing handoff fixture retains its transaction
outcome matrices. Worker runs use `--native-source-root` to reference the main
checkout's read-only `decompiled` folder without copying game references.

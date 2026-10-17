# Inert owned shader-bank experiment

This tool accepts only the tiny owned `CoverageBank/Original` fixture profile.
It is not installed, called by the mod, or authorized to modify native game
shaders. It never writes its source bundle or an existing output.

`derive.py` loads a bounded, SHA-pinned, dependency-free UnityFS bank containing
exactly one Shader and one AssetBundle object. It retains the complete compiled
program blob, platform/program tables, pass state, properties, keywords, local
references and source bytes. It changes only:

- The owned shader's parsed name to `CoverageBank/Derived`.
- The one literal source-alpha factor from SrcAlpha(5) to One(1); destination
  alpha remains OneMinusSrcAlpha(10), as do both original RGB factors.
- The derivative bank's two name fields and its deterministic new CAB identity.

The saved output is reparsed and compared against the full expected trees,
including signed-zero-sensitive canonical fields. External references, shader/
bank dependencies, extra objects/passes, property-controlled blend state, other
source/profile names and any unapproved program/state mutation are refused.
This closed profile is deliberate; it is not a general native shader allowlist.

Run the complete small experiment with the existing Unity and UnityPy tools:

```sh
python3 tests/scene-coverage-shader-bank/run.py \
  --output .planning/debug/steamlink-shader-bank-worker
```

The runner builds an owned Linux/Vulkan shader bank, derives it without shader
recompilation, runs causal binary controls, and loads/rasterizes both banks
simultaneously. It freezes tool/fixture inputs and source bundle identity.
Every child is reaped before the shared retention API and exact owned-cache
fallback execute. Small generated banks, source receipts and logs remain private.
See [the scoped result](../../docs/research/steamlink-shader-bank-20261017.md).

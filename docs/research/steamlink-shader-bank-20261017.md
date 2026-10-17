# Owned compiled-shader bank derivative proof

A new, separate AssetBundle can preserve a compiled shader's RGB programs while
changing its independently selected alpha blend state. This mechanism now has
an actual owned Unity GPU proof; no native D3D11 hook or second scene render is
required for this finite case. No game shader, game file, production asset bank,
mod rendering path, Steam sender or Frame has been changed.

## What was constructed

The new owned `CoverageBank/Original` shader has a straight over pass, original
clip branch and literal RGB/alpha factors SrcAlpha/OneMinusSrcAlpha. Unity
2021.3.5f1 builds it for StandaloneLinux64/Vulkan. The original bank contains
one Shader and one AssetBundle object, local references only, and no external,
shader or bank dependencies.

[The offline derivative tool](../../tools/scene-coverage-shader-bank/derive.py)
requires its exact observed source-bank SHA before parsing. It makes a separate
memory copy and writes only a previously absent output. The new bank uses a
new deterministic CAB identity and bank name; its shader has a distinct parsed
name. Local object path IDs and asset paths stay intact inside the new CAB.
Original and derived banks can consequently coexist and resolve distinct shader
objects rather than replacing a source or relying on Shader.Find collisions.

Only the pass's **source alpha factor** changes from SrcAlpha(5) to One(1).
Destination alpha was already OneMinusSrcAlpha(10) and stays unchanged. RGB
factors, output code, vertex code, clip, depth, stencil, culling, properties,
keywords, program tables and every other parsed shader field are retained.
The complete 762-byte compiled blob has SHA256
`1a104064f78b3b436764e6b1492ded2b6959b6d8813e0f933d2d95a9ad69bd93`
in both banks. No textual candidate shader is compiled.

The output is reparsed. Full original/candidate Shader trees are compared with
only the two allowed shader changes applied to the expectation; complete
AssetBundle trees differ only in their two name fields. Program/offset/extent
metadata is included, not just a compressed-blob hash. Canonical comparisons
retain signed zero and reject non-finite JSON state rather than using loose
numeric equality. The source remains byte-for-byte unchanged.

## Actual GPU and refusal evidence

The frozen end-to-end run is privately retained at
`.planning/debug/steamlink-shader-bank-worker-20261017/final/run-yjoi7js4/`.
Unity2021.3.5f1, Intel RPL-P Vulkan, Gamma executes **8 cases / 6,172 checks**:
one/two half-opacity layers, with/without the original clip, on ARGB32 and
ARGBFloat single-sample targets. Both banks and distinct named Shader objects
are loaded simultaneously. Every RGB component across each complete 16x16
image is compared by its float bit pattern. All are identical. Original alpha
remains .25/.375; derived alpha is .5/.75, with format-appropriate quantization
tolerance. Clipped pixels remain empty.

The final original-bank SHA is
`867be17d928b25c943ad072b70d073994e35342b822349f602d34bbcb2375fb1`;
derivative SHA is
`6530d97c01376e107771bff9cb861c9c0c4aed9e0027f62c5a1661a89a5ca64e`.
The run records the actual Unity/device/color-space identity, copied source pins,
compiled-blob equality, complete parsed-state equality and immutable originals.

The binary causal suite passes **18 checks / 14 refusals**: wrong source pin,
overwriting source/existing output, altered compiled program, RGB factor,
depth state, program/clip metadata, alpha factor, shader identity, bank
dependency, serialized external, CAB identity, source program and a repinned
source whose alpha state is property-controlled instead of the literal profile.
Mutants use real reparsed/saved copies; temporary mutant banks are removed.

An initial setup failure is retained: the empty project package manifest let
BuildPipeline return success while reporting AssetBundle/AssetBundleManifest
module disablement and producing shader-only content. This is not a valid
bundle proof. Explicit builtin assetbundle/jsonserialize module dependencies
repair the fixture; the complete tool independently refuses missing/extra
objects before candidate work. A first successful pixel exploration used exact
finite float equality; the final frozen runner strengthens that to explicit
bit comparisons. These finite fixture repairs are not game or headset causes.

Every Unity/Python child is reaped before the shared completion-cleanup API and
exact owned-cache fallback run. Library/Temp/Obj/Logs caches are gone after each
Unity stage. Small compiled owned banks, source files, manifests, results and
failure logs are intentionally retained. There is no new native asset scan or
large extraction.

## What this does and does not establish

The copied-program/new-bank approach is feasible for this owned, closed,
single-pass Vulkan shader. It makes an alpha-only derivative possible without
rewriting its RGB equations, patching the game's installed files or intercepting
D3D11 blend commands. The current tool deliberately accepts **only this fixture
identity/profile**, not arbitrary matching material names or caller booleans.

It does not establish native game shader support, Windows D3D11 execution,
multiple dependent shaders, fallback/GrabPass or external texture closures,
stripped native variants, additional pass/RT layouts, alpha feedback, UI render
stacks, MSAA edges, HDR, stereo/XR or a live mask. No runtime adoption exists.
The earlier four-family and independent depth-stamp evidence retain their own
finite scopes; they are not repeated or silently combined into an all-game pass.

The next bounded extension would select one exact original native transparent
family/source/pass, prove its actual alpha/RGB/feedback semantics, and generate
an independently named **mod-owned** derivative bank from the user's local game
bytes. It must retain every original program/dependency and changed-state proof,
with a cold source-versioned cache and runtime ownership/restoration plan. An
actual Windows/D3D11 pixel proof is still required before making that backend
claim. Unknown dependencies, variants or pass semantics stay refused; do not
weaken this fixture's closure checks merely to ingest a whole native bundle.

This alpha-state mechanism complements same-eye opaque-depth stamping. It does
not make the game's ordinary framebuffer alpha trustworthy by itself. Final
scene data still needs contribution RGB including emission at A0, correct
coverage, exact intermediate/UI handling and one coherent eye/source/resolve.

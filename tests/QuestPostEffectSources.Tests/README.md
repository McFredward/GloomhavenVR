# Legacy post-effect source checks

`python3 scripts/quest-post-effect-source-tests.py` runs the actual builder module
with synthetic archive and generated-project seams. It covers source identity,
archive selection/bounds, original GUID preservation, cache reuse and transaction
failure without distributing Unity's legacy shader source or game assets.

An optional private `--official-installer PATH --recovered-project PATH` proof
uses the pinned official Unity Standard Assets package and the exact recovered
placeholder exports. It copies only selected shader files into a temporary
private fixture, invokes production extraction/staging, and records hashes/pass
contracts. It never opens Unity or modifies the recovered input project. Passing
these checks does not establish shader compilation or headset pixel parity.

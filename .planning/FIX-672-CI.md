# Build672 dev CI evidence

The fresh full CI run completed successfully on2026-10-12:
[CI38275346337](https://github.com/McFredward/GloomhavenVR/actions/runs/38275346337).

Exact tested commit: `d6f26b4e0a4d51d711fda27748414e4811d3b4d7`.
The source-changing push ran full validation, without source-proof reuse:
source/build, installer smoke tests, all four runtime shards and the final full
source-tree proof job passed. The CI inventory contains98 suites. Wire vectors
are compiled on hosted CI; actual300518 runtime assertions were executed locally
against the real Unity runtime, as recorded in [FIX-672-REVIEW.md](FIX-672-REVIEW.md).

Final local evidence is retained in the main checkout under
`.planning/debug/fix672/` and `.planning/debug/dev672/`. The integration archive
receipt verifies23424 files/1421874823 bytes, including native source/capture,
failed-control, compiled-snapshot and worker proof. The current dev Debug
plugin/preloader remain in their usual `src/*/bin/Debug/net472` paths; compact
Debug/Release assembly hashes and the required new main-bank hash are recorded
under `dev672/assemblies/manifest.json`. No release/tag/archive was published.

All four temporary Build672 worktrees were removed after their source integration
and verified evidence retention. Owned project caches were cleaned through the
completion API after reaping. The completed private Wine prefix lease was retired
with its original pin/registry provenance retained; shared Wine state, other
worktrees, game references and hardware inputs were not touched. The final root
build-cache receipt removes128159171 bytes with no skipped paths. Current Debug
deliverables, the published1.1.0 archive and original evidence remain available.

The follow-up commits add only this CI record and update internal Markdown
state/review. Their source code, assets, installer and test contracts are unchanged
from the successful exact-source full run above. These new `.planning/` review
paths are outside the approved documentation-reuse whitelist, so the hosted
pipeline runs full validation again. No CI policy is weakened for this repair;
the final run/status is retained under `dev672/` after completion.
Actual Frame installation and HMD stereo/presentation remain separate hardware
verification; Wine/GL/D3D and hosted green checks do not establish a headset image.

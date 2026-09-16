# Release 1.0.3 — build 515

## Authorization and source

The maintainer accepted the current changes and explicitly authorized release 1.0.3.
The accepted runtime/assets source is dev commit `704c9cfe639c6d046dacf64638bcfa7e2250c2ca`.
The final candidate `b53e79a7bcfff326325b80d24d797d8175d032f7` adds only the bilingual
player-facing release highlights. That documentation-only commit skips redundant CI; the CI
run for the unchanged runtime/assets source succeeded:
[35151193083](https://github.com/McFredward/GloomhavenVR/actions/runs/35151193083).

PR [#6](https://github.com/McFredward/GloomhavenVR/pull/6) was merged using a merge commit,
`fd76de86b683e4c99f071ef74384642d0420647b`. Its tree matches the candidate exactly:
`f99a0aeac444146d00f87fe6e64d9b85a3c85223`. The production main/dev provenance check passes.
No runtime or asset changes were made after maintainer acceptance, and no branch was force-pushed.

The release includes first-tutorial-only additional VR lessons and softer Glove finger relief.
The player summary explicitly requests extracting the complete archive because the hand
materials changed in the asset bundle. The existing main release workflow is unchanged;
the earlier discussion of reducing duplicate main-branch tests did not result in a workflow change.

## Validation and acceptance limits

- All 17 local guard checkers and production suites pass; 254,019 wire assertions.
- Strict Release: zero warnings and errors. Bilingual docs and whitespace checks pass.
- Tutorial scope: 42 runtime assertions, 17 production bindings, seven runtime negative
  controls and one binding negative control; see [TUTORIAL-SCOPE-514.md](TUTORIAL-SCOPE-514.md).
- Hand changes: native comparison renders and actual Unity bundle loads validate all six
  hand prefabs and their 19 anchors each. Existing mesh/texture/rig content is unchanged;
  see [GLOVE-SURFACE-515.md](GLOVE-SURFACE-515.md) for exact semantic/raw differences.
- The complete bundle has 617 assets, 74,942,975 bytes and SHA256
  `fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
- Release topology simulation: 36 assertions pass.
- The maintainer's acceptance supersedes the previously pending overall hardware decision.
  No new hardware log capture or individually enumerated edge-case results were supplied
  with this release request. Automated/native-editor checks alone do not prove headset pixels.

## Publication

Main-only Release run
[35151926513](https://github.com/McFredward/GloomhavenVR/actions/runs/35151926513)
completed successfully. The public release was published at `2026-09-16T21:31:37Z`:
[1.0.3](https://github.com/McFredward/GloomhavenVR/releases/tag/v1.0.3).

- Annotated tag `v1.0.3` resolves to the exact main merge `fd76de86b683e4c99f071ef74384642d0420647b`.
- Anonymous GitHub `releases/latest` returns `v1.0.3`, neither draft nor prerelease.
- The anonymously downloaded `GloomhavenVR-1.0.3.zip` contains 29 entries, passes ZIP CRC
  verification and is 85,115,146 bytes. Its SHA256 matches the published asset digest:
  `d8cd838023d77130a5266cca1c8d3045c9a65480b78bdef7d86b6ec96061ab78`.
- The bundled asset is exactly 74,942,975 bytes and matches the accepted bundle SHA256 above.
- Decompiled public DLL constants report version `1.0.3`, commit `fd76de8`,
  `IsDevBuild=false` and `ModBuild=515`.
- Bot merge `0fadc293` retains release-main ancestry on dev; bot commit `7e25ce54`
  advances its next version to `1.0.4`. The local dev checkout fast-forwarded to those commits.
- This audit and state update are documentation-only dev bookkeeping; publication is not rerun.

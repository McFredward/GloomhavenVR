# Quest 1.2.0 minimal-update qualification

The maintainer requested rebasing the Quest implementation onto the released
`main` 1.2.0 source, while retaining the established compact APK update path.
This is an update compatibility review, not a new full game import or a hardware
acceptance report.

## Inputs and boundaries

- Previous public Builder: B713, source `f4539f297a948476b593f6fd0ebc561f04e22a8e`.
- Previous public archive SHA-256:
  `fa0a762d7a6a38ea23644090947b122f6f86acbb067d2864bb19955d79f42a01`.
- The worker starts from current `dev` `fd45d606f`; the integration checkout is
  independently owned by the primary agent. This worker does not edit its source.
- Canonical evidence is retained outside the checkout at
  `/home/claw/quest3-local/build/evidence/quest120-rebase-20261021/update/`.

## Required reuse

Changes to C# implementation, the mod version and embedded authored resources are
inputs to compact IL2CPP compilation. They require a matched code/metadata pair;
they do not establish that Campaign scenes, textures, shaders or original game
media need conversion again. Unknown changes to those independent producer
boundaries must continue to request an explicit rebuild decision.

The established original game producer identity is
`6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1`;
its original-prefix identity is
`cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f`.
Both identities must remain unchanged for this rebase.

Movies are independent media delivery inputs. Existing SBS media and numeric
depth recipes must retain their producer identity. This rebase must not run a
depth model or regenerate existing movie files. The original-derived environment
bank and separate figure/audio banks likewise retain independently qualified
producer identities.

The released main environment generator predates Quest's owned-PC extraction,
per-mesh derivative checkpoints, and source/bank provenance interfaces. Its older
interface cannot replace the Quest implementations merely to resolve a rebase
conflict: that would invalidate art identity and remove required APIs. The Quest
generator, `environment_bank.py`, `roles.py`, `geometry.py` and `export-native.py`
are retained while incorporating independent current mod changes.

## Source and package review

The frozen integration source at `16cb4394cce03c4e55dbaa500f1462baf65ac1d9`
is semantic version **1.2.0, ModBuild716**, based on released main
`4f10ddd60`. Its public source selection contains **3,640files / 2,094,576,495bytes**,
1,283,582bytes less than B713. These are source input bytes, not an APK size.

The first actual source-selection check caught a packaging incompatibility:
the old Builder's explicit required resources still included the retired Windows
`stream-quality.exe`, `compositor-state-probe.exe` and
`projection-overlay-probe.exe`. Main1.2.0 removes their embedded-resource entries
and executables. The primary agent removed exactly those three obsolete names
from the source release allowlist; it did not restore obsolete executables or
weaken original payload qualification. The remaining authored scene shader,
connection reader and Valve loader retain their actual existing resource roles.
`SteamLinkConnectionLifecycle.Tick` still admits its Windows helper only for
`WindowsPlayer` and `SteamVR/OpenXR`; `ReadWindows` independently rejects other
platforms. These resources are not Android procedural native plugins.

## Actual compact-update result

`qualify-source.py` loads both actual production Builders through their isolated
installer loader with a hostile pre-existing `storage` module and stdlib
`profile`. The source selection uses the actual release selector and authored
link materialization. The previous rows come from the delivered B713 ZIP, and
the production planner reads the exact new selected source bytes. Module aliases
and the search path are restored. Game input rows are controlled metadata only;
no original game bank is opened by this qualification.

The actual `update_requirements` result is:

| Boundary | Result |
| --- | --- |
| Update strategy | `compact-code` |
| Matched native code/metadata compilation | Required |
| Full rebuild reasons | None |
| Authored/environment art changed | False |
| Procedural native helper changed | False |
| Figure banks changed | False |
| Corrected audio banks changed | False |
| Stereo movie banks changed | False |
| Original payload producer rows changed | None |

The complete original producer and original-prefix identities above both match
B713. The exact movie, figure and audio plans match when supplied the same
controlled original file identities; their producer keys are respectively
`d02fafcf19d25e1e5aa1510560eec2bd1276fca31b3ad1925e2bf2f9f85e1d0e`,
`9e83bcaa25b776b5e1ec25f51d3885fa77e9668a02569cf1cf227961ec3041dc`,
and `148b2dc71dd02058deaf06c3a6f55531d2919499630169b7c7ec30fd8a5a02ec`.
Existing original conversion, environment derivatives, movies, figure banks and
corrected audio remain reusable. The normal APK publication/signing step still
writes an updated APK; code-only does not mean a DLL can be substituted into an
IL2CPP APK without matching native compilation.

## Focused validation and limitations

Seven relevant host groups pass **108tests**:

- Rebased update scope13; update orchestration48; movie file delivery5.
- Owned environment bank19; isolated Wizard figure loader5; source metadata5.
- Release completeness/admission13 (`ReleaseTests` only; unrelated support export
  tests were not rerun).

The environment group initially used the host Python without `UnityPy` and failed
on that missing test dependency. Its unchanged source passes all19tests with the
existing `/home/claw/unitypy-venv/bin/python`; both attempts remain in evidence.
All fixture processes exited and were reaped; every invocation called
`cleanup_completed_run`. Their temporary folders self-cleaned and no generated
Unity cache was left by these host-only checks.

The exact selected rows, changed compiler rows, producer identities and planner
result are in `source-update-classification.json` and
`source-release-manifest.json`. Test logs and source SHA-256 pins are alongside
them. This is focused evidence, not a new complete local gameplay gate. No full
Campaign conversion, Unity import, shader compilation, depth estimation, APK
build, device installation or headset visual outcome is claimed here. A final
delivered ZIP must still have its selected rows compared with this frozen
manifest and pass the ordinary game-free archive verification.

# Quest Builder download preflight — 2026-10-09

## Implemented boundary

The Wizard checks the known missing download endpoints before entering its long
conversion stages. It reads small recipe declarations, pinned archive sizes and
local tool markers; it does not launch compilers or hash game/recovered assets.
Each producer retains authority over actual hashes, licensing and output reuse.
The check is repeated on a new run, including a retry, so a fixed connection can
continue the same session without discarding completed work.

Windows x64 and Linux x86-64 select their own locked tools. Full builds include
known late exporter, TMP, shader, OpenXR, Proton/FEX and Opus acquisitions.
Profile-only updates omit Unity, .NET and asset-conversion prerequisites;
build-only mode omits ADB. Existing selected/discovered Unity Hub and ordinary
ADB locations, including SideQuest, are recognized without requiring their
download. Matching cached artifacts avoid endpoint requests.

Four bounded requests run concurrently, using the actual HTTPS recipe URLs and
the normal trusted certificate/proxy environment. A byte-range GET reads at most
one response byte. HTTPS redirects remain HTTPS, and signed redirect queries are
not written to logs. Declared alternatives are tried only for recipes with an
actual fallback. There is no ping to an unrelated Internet connectivity service.

An unreachable mandatory dependency blocks before conversion with its name and
an actionable English/German message. HTTP 404/410 is described as an unavailable
pinned dependency, rather than telling the user to fix their Internet connection.
Retries preserve the existing workspace. NuGet/UPM cache directory presence
cannot certify their transitive closure: where a shared cache may already suffice,
failed catalog probes are conditional observations, not an early rejection of a
potentially local build. Compiler/package errors remain visible through the
existing producer failure path.

The persistent `sessions/<id>/network-preflight.json` records successful and
failed checks and is included as `wizard/network-preflight.json` in support ZIPs.
`offlineClosureVerified` is always false: endpoint reachability and local presence
are hints, not a verified offline-ready certificate. Launcher Python provisioning
can itself require Internet before the browser Wizard exists. A future dependency
preparation/offline operation is specified separately in
[the offline feasibility audit](QUEST-OFFLINE-BUILDER-20261009.md).

## Actual endpoint evidence

A cold Linux recipe check on 2026-10-09 requests only tiny response samples;
GitHub Opus, Unity installers and most other pinned endpoints are reachable.
The pinned Launchpad FEX `fex-emu-wine_2609.1-1~n_arm64.deb` endpoint returns HTTP
404. A new workstation lacking that artifact therefore receives an early named
dependency failure. No native pin was substituted, and no different FEX build
was silently accepted. The supplied 070011 Windows run has already committed
its native runtime, so its exact local cache can continue without that download.

This availability finding reinforces the need for a stable, hash-qualified
upstream artifact store in the offline design. It does not establish that every
later restore, large download, activation or APK export will succeed. Actual
compact endpoint evidence is retained outside the handoff folder at
`/home/claw/quest3-local/build/evidence/B647-network-preflight-20261009/`.

## Focused verification

- 23 preflight tests cover operation-specific dependencies, actual recipe and
  requirements identities, old native archive keys, local no-network paths,
  bounded fallback, cancellation, unavailable pins, retry and failure persistence.
- Real ephemeral HTTPS servers verify an untrusted certificate is rejected,
  an explicitly trusted CA succeeds with `CERT_REQUIRED`, and an HTTPS-to-HTTP
  redirect is rejected. These controls do not disable verification.
- Support integration verifies the structured preflight report survives log
  rollover and is exported through the authenticated existing support endpoint.
- Existing Wizard retry tests qualify the integration; exact counts and extracted
  release evidence are recorded alongside the final delivery.

No Unity import, full shader audit, new APK or Windows end-to-end build is asserted
by these checks. The separate retained-result qualification changes preserve
the current game/producer identity boundary.

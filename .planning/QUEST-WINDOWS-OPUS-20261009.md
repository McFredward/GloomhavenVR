# Windows native-runtime HTTPS retry

The supplied `quest-build-support-20261009T054817Z-c6ac43d2.zip` identifies
source `085e037ce` / ModBuild 647 on Windows AMD64. Recovery reports reuse. Twelve
preparation checkpoints complete through `file-extras`; `native-runtime` then
fails with `CERTIFICATE_VERIFY_FAILED: unable to get local issuer certificate`.
The Proton static audit has already passed and its interpreted payload was
staged. The next source-proven call downloads the original Opus voice archive.
The capture has no URL traceback or Windows certificate-chain dump, so the
particular missing issuer is not established.

## Repair and retained work

- Prefer Xiph's actual [Opus 1.5.2 release archive](https://github.com/xiph/opus/releases/tag/v1.5.2),
  with one bounded fallback to the existing official Xiph endpoint on transport
  failure. Keep normal TLS validation, the unchanged SHA256 and exact 7,839,412
  byte size. Reject insecure redirects and failed content pins without trying to
  reinterpret them as a transport problem.
- Report requested/final download hosts, real streamed-byte progress and bounded
  actionable errors; signed CDN query parameters are omitted. Download progress
  cannot close the parent native-runtime checkpoint.
- Exclude this one native source helper from the original preparation prefix
  only while the ordered journal proves native-runtime has not committed. All
  earlier producer/game/profile/template identities remain qualified. Once
  native-runtime closes, its helper again qualifies the completed output.
- The Wizard distinguishes certificate failures from a generic Python exit and
  explicitly retains workspace/conversion guidance in English and German.

## Evidence and limits

The actual official archive downloaded and the Android ARM64 codec compiled in
2.641 seconds using the existing conversion environment and selected Unity NDK.
The unchanged original archive hash and all 12 original API and four CTL exports
were checked; a warm retry with networking prohibited reused the native cache.
Actual local TLS fixtures reject an untrusted primary, accept a trusted fallback,
and reject HTTPS-to-HTTP redirects. Fifteen native/download, twenty network audit
and ten network smoke cases pass, with no skipped cases.

Thirteen preparation identity cases include an actual ordered twelve-checkpoint
retry reaching only the native producer and then the next audio producer, with
the original Unity Library retained. Completed-native and unknown earlier
producer changes are negative controls. Forty-two preparation-journal, fifty-five
recovery-resume, eleven failure, forty-six Wizard flow and twenty-four
release/support cases cover the affected boundaries. Recovery tests use the
existing private conversion environment containing PyYAML; the stdlib launcher
is not a conversion test environment.

Source/archive selector evidence uses the actual old/new delivery manifests and
immutable Builder bytes with a reconstructed twelve-step journal; it is not a
copy of the player's missing Windows project. Private evidence is under
`quest3-local/build/evidence/B647-support-20261009-054817/` and
`quest3-local/build/evidence/native-download-20261009/`. This Builder-only repair
keeps ModBuild 647 and the inherited runtime evidence. No new whole-mod gate,
Windows end-to-end APK, broad shader audit or headset outcome is claimed.

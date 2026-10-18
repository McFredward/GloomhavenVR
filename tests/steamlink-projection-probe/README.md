# Explicit Steam Link projection-delivery preview checks

Run `python3 tests/steamlink-projection-probe/run.py --output <evidence-directory>`.

This bounded suite executes the actual preview owner, strict readiness parser, UI callbacks
and actual `SceneHeadOutput` callback bodies. Process/pipe and GPU boundaries are supplied
explicitly. The actual owner/UI/localization compile against pinned Unity 2021.3.5 and .NET
Framework 4.7.2 metadata. Synthetic metadata is not a Windows/OpenVR transcript. Nine
semantic mutations prove Debug/source gates, immediate launch without Tick/menu close,
menu reopening, prompt ownership revocation, automatic options closure, unchanged input and local-metadata retention without readiness.

The preview starts its owned helper directly on an accepted click and closes VR Options.
A failed automatic close does not delay launch and leaves a separate Stop action available.
Initial menu visibility is tolerated only until its first actual close; reopening ends the
preview. Duplicate/stale starts cannot overlap helpers or hide the controls. Permission loss,
EOF, exit and malformed/over-budget output retire the owned child without automatic retries.
Unknown child ownership remains sticky. The teardown grace only retires the child and pipes;
it never disables MR or restarts Steam/SteamVR.

The preview NEVER allocates/clears/replaces an image, changes a camera or changes the carrier.
The game's original source is packed as usual before AND after helper API readiness. Ready
is a local API receipt, not proof that Steam Link delivered the overlay to the headset.
There is no zero-input witness or independent-alpha/quality success claim in this preview.
No helper/OpenVR API, Steam setting or headset is used by this fixture. Frozen native overlay
poses require a still head during a later explicitly requested visual check.

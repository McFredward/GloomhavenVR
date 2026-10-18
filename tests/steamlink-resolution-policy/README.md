# Actual-carrier Steam Link resolution policy

Run `python3 tests/steamlink-resolution-policy/run.py`.

The fixture executes the production pure policy against the Build 701 4040-pixel
regression, changing actual allocations, portrait/landscape carriers, all 16384
accepted axes, every 64-pixel alignment phase and invalid/overflow inputs. Four
compiled negative controls reproduce a fixed 3072 request, a 4096 ceiling,
rounding down and selection of the shorter axis. The production class also
typechecks against net472 with warnings as errors; no Unity boundary is stubbed.

The policy only derives an aligned uniform encoder request from actual current
carrier dimensions. It does not change render quality, saved configuration, XR
allocation or viewport. It removes the newly introduced fixed 3072 transport
ceiling. It cannot restore samples already lost when the v3 atlas puts its
2048-square scene inside a 3168-square carrier, nor prove the final negotiated
video size. No SteamVR write, Frame action, codec or headset picture is tested.

Generated source snapshots, logs, receipts and results are retained. Completed
compiler caches are removed through the common completion-cleanup API.

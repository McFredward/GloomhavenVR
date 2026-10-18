# Explicit Steam Link projection probe checks

Run `python3 tests/steamlink-projection-probe/run.py --output <evidence-directory>`.

This bounded suite compiles and executes the actual probe owner, strict readiness parser,
per-frame input latch and actual `SceneHeadOutput` callback bodies. The process/pipe and GPU
boundaries are supplied explicitly; the actual probe/UI/localization compile against pinned
Unity 2021.3.5 and .NET Framework 4.7.2 metadata. Synthetic helper metadata is not a Windows
or OpenVR transcript. Six semantic mutations prove the Debug/source gates, first menu close,
same-frame eye latch, immediate revocation before delayed cleanup and original-source retry.

The fixture covers pending, ready, menu reopening, live permission loss, late start, process
exit, EOF, bounded malformed output, explicit retry, unknown ownership, metadata retention,
zero-input loss, actual-source host admission and an isolated failed probe pack. It runs no
native process, queries no OpenVR API, modifies no Steam settings and contacts no headset.
The separate `steamlink-projection-witness` suite is responsible for the real shader/GPU
zero-input witness. Neither suite establishes independent overlay delivery through Steam Link,
a correctly reprojected moving-head picture, or a production full-quality rendering solution.

The runtime action is intentionally temporary, only under **Advanced > Cheats**, and requires
Debug plus an actually admitted robust-v3 native Frame/Steam Link MR source. The helper does
not expire; reopening VR Options, disabling permission/MR or ending VR requests stdin EOF.
Its finite teardown grace only releases the child and pipes. It never disables MR or restarts
Steam/SteamVR. A diagnostic pack refusal retries the untouched actual source and does not
poison ordinary MR. Both eyes finish a decision already latched for the current XR frame.

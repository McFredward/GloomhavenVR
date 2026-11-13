# Windows large-APK alignment repair — 2026-10-11

## Evidence and cause

`quest-build-support-20261011T085642Z-19f134e1.zip` identifies the corrected
`9aa4b474b` Builder, new-build Runtime 668. The run retains its completed 665
Unity Player and reaches final signing in 5.187 seconds. Windows zipalign 30.0.2
still refuses the temporary capability APK before opening its ZIP directory.
The current native-tool log is now included and its message is visible.

The preceding ZIP32 correction passes the large native Linux check, but that
did not exercise the Windows executable. A second limit is present in Google's
official 30.0.2 Windows binary: its PE i386 reader uses 32-bit `fseek`/`ftell` and
a signed comparison against the 22-byte minimum ZIP footer. It cannot represent
EOF above 2 GiB. The pinned AOSP Android 11 reader has the same sequence. The
downloaded official SDK archive matches Google's repository-manifest checksum;
binary imports and the disassembled reader verify this limit independently.
The actual Windows APK is not in the capture, and no Windows runtime execution
is claimed.

Primary references:

- [AOSP Android 11 ZIP reader](https://android.googlesource.com/platform/build/+/android-11.0.0_r1/tools/zipalign/ZipFile.cpp)
- [Android APK alignment contract](https://developer.android.com/tools/zipalign)
- [Microsoft file-position functions](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/ftell-ftelli64?view=msvc-170)

## Repair and minimum repeated work

The existing raw APK packaging pass aligns stored local data to 4 bytes and
uncompressed native libraries to 16 KiB, preserving compressed member bodies,
CRC/size/method, original metadata and comments. Python's full-width offsets
handle valid ZIP32 positions above 2 GiB. A bounded header checker verifies
actual offsets, local/central records, ZIP32 descriptors, limits and overlap.
This replaces the fallible Windows alignment command and removes its additional
whole-APK copying pass. Real Java apksigner signing and signature verification
remain required; alignment is checked again after signing. Existing valid 4-KiB
outputs remain acceptable without rewriting them merely to adopt 16-KiB padding.

The previously compiled Player, original bank, frozen inputs and private signing
key retain their owners. Only the incomplete capability/signing owner runs on
this retry. Independent completed APK, Shader and Compute gate receipts remain
available. No earlier conversion/import, Unity invocation, source inventory or
new game APK export is required for this repair. Exact reviewed late-consumer
aliases preserve completed preparation across the corrected Builder delivery;
unknown producer changes remain distinct.

## Validation limits

The final functional tree passes 333 focused host checks, including the 14 new
alignment cases, mandatory native signing, damaged-header rejection and retained
Player/preparation continuation. Independent read-only review additionally
compares 40 small raw-copy archives against the pinned native Linux alignment
oracle. These checks cover alignment rather than a fresh game build.

Focused final-tree signing/alignment and continuation counts, independent review,
the real retained large-Player/native oracle, actual release compatibility,
Linux launcher restart, archive hash and cleanup measurements are recorded in
the handoff validation receipt. The large proof uses the retained Runtime 625
Player and does not establish Runtime 665 playability or headset rendering.
Windows completion and the resulting hardware test remain pending.

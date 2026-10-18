# Native content archive repack fixture

`run.py --unity EDITOR_DATA --packages SCRIPT_ASSEMBLIES --bundle REAL_LZ4_BANK
--output NEW_PRIVATE_DIRECTORY` compiles the entire actual Editor source against
the installed same-version Unity/package SDK, then executes only its ZIP methods
on that Unity installation's Mono. No Editor process is started. The execution
assembly substitutes Newtonsoft-backed JSON and log calls for Unity's native
JsonUtility/Debug icalls;
the separate SDK compilation uses the actual native API declaration without that
shim. Native Addressables setup/import/build and headset behavior are outside this
fixture's proof scope.

The fixture starts the real standard-library Python packer through the actual C#
ProcessStartInfo seam, using private paths with spaces and `&100%`. It uses one
already generated, unchanged UnityFS LZ4/LZ4HC bank below
16MiB. Exact hashes, size, ZIP compression method, unchanged-file reuse, metadata
and entry-set defects, source-byte changes and copy-time integrity are checked.
Same-runtime Mono measurements document whether its NoCompression enum really
changes compression; independent CPython measurements compare Deflate and Stored
with identical native bytes and record the resulting sizes/hashes. They do not predict the full
Campaign archive's time or size. Proprietary bank and generated evidence remain
private; no original payload is tracked.

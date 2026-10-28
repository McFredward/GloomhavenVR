# Portable original compiler seed for Quest updates

## Scope and permission

The maintainer explicitly permits a qualified installed Quest game to replace
the former mandatory PC installation for an update. This is a local convenience
policy, not proof of purchase or an anti-piracy boundary. Original game bytes
must not be added to the game-free Builder release.

This lane owns only `tools/quest-builder/update_seed.py`, its focused host tests
and this review. Its worker starts at Quest feature checkpoint `6f5c94ace`.
Acquisition, signature qualification, native compilation, installation and UI
integration belong to the primary agent and separate lanes.

## Why an old installed APK is insufficient for a code update

The existing ARM64 player is IL2CPP. Its script roster, property hashes and
global metadata establish compatibility but do not contain the original Mono IL
needed by the weaver/compiler. An original reference assembly generated from
metadata would not recover method bodies. No such substitute is introduced.

Therefore B717 and older installations need one PC-assisted update to add this
seed. A qualified newer installation can supply it on a different computer;
the original PC game installation and full old Unity project are then unnecessary
for supported compact updates. Unity/Android compiler tools and temporary output
space can still be needed. A future change requiring original asset production
may separately require the PC originals; the seed promises only managed IL.

## Exact payload and closure

The producer reads the immutable original input manifest, not updated/woven DLLs
or an arbitrary directory search. Every `Managed/` entry must be a supported DLL;
unknown non-DLL managed dependencies fail explicitly rather than disappearing.
Current real input contains 169 DLLs totaling 47,283,392 uncompressed bytes.
No other managed dependencies occur in that inventory.

The output is `assets/Quest/update-seed.zip` plus
`assets/Quest/update-seed.json`. The ZIP has deterministic sorted member names,
fixed timestamps, ordinary file modes and compression level 1. Each exact name,
size and original SHA-256 is bound by the external signed APK manifest. The
descriptor additionally binds original inputKey, managed inventory hash and
archive SHA/size. It contains no profile, launcher identity, keys, saved games,
app data or original asset banks. The Builder ships only the producer source.

Bounds are 1,024 DLLs, 64 MiB per original DLL, 256 MiB total or seed archive and
2 MiB JSON. Reject duplicate/case-ambiguous paths, traversal, Windows reserved
names, directories, links, encryption and unsupported compression. Duplicate
JSON keys are invalid. A present-but-broken seed is never treated as permission
to invent compiler inputs. Missing legacy seeds have an actionable one-time-PC
message; a PC-assisted update can repair a damaged seed.

## API and cache behavior

- `build(builder, data, inputs, output)` returns APK replacements: a Path for the
  ZIP and canonical bytes for its descriptor. Read each original DLL while
  archiving and qualify its SHA and unchanged file identity around that read.
- `inspect_seed(apk, base)` reads only bounded descriptor/ZIP metadata, returning
  `available`, `state`, byte sizes and a reason. It does not hash DLLs or banks.
  `base` is the caller's existing original APK inspection result.
- `extract(builder, apk, base, output, progress=...)` returns an owned `dataRoot`
  retaining the `Managed/` layout and a stable identity proof. The caller must
  qualify the APK/signature and hold the workspace lock before consumption.
  Discovery alone does not establish full seed-byte qualification.

Producer and extracted caches have exact durable ownership markers. Never
overwrite an unowned directory, borrowed link or hardlinked target. A bounded
closed-byte receipt permits stamp-only reuse: POSIX inode/ctime, or the existing
Windows storage helper's NTFS/REFS FileId and ChangeTime. Unknown file systems
fall back to exact bounded hash reads; creation time is not persistent trust.

An altered DLL is rehashed and only that DLL is extracted again. A corrupted
cached seed ZIP is repaired separately, preserving good extracted DLLs. Missing
or damaged optional proofs cause bounded requalification, not game reconversion.
Checkpoint every 16 members; after interruption, already extracted files without
a final receipt are hashed and retained if exact. Source APK changes during the
operation are rejected. Stable returned proof fields exclude timestamps, paths,
timings and reuse counters, keeping update identity independent of cache state.

## Focused evidence and limits

Evidence is retained outside the worker under
`/home/claw/quest3-local/build/evidence/quest-device-update-20261021/seed/`.
The unit suite exercises the production module, real ZIP creation/extraction and
real file metadata with synthetic DLL byte fixtures. It covers deterministic
delivery, legacy discovery, input/descriptor/archive/member hash mismatches,
ownership, unsafe paths and links, single-file repair, optional proof repair,
unsupported file systems and a deliberately interrupted 23-file extraction.
Negative controls deny all hashing/copying for an unchanged repeat, deny recopying
for stamp-only changes, and alter a real source while it is being read.

The additional qualification uses the actual selected original 169-DLL producer
inputs, never synthetic replacements for their bodies. It creates and extracts
the seed, repeats with file hashes/copies forbidden, checks stable returned
identity, and pins the input/seed/source identities. It does not invoke Unity,
the weaver, IL2CPP or a headset. Generated DLL/seed/APK test copies are removed
after preserving logs, source snapshots and hash manifests; common completion
cleanup runs only after the host test child is reaped.

Final lane result: 23/23 host tests pass. Two independent builds of the actual
original inputs produce identical 18,389,781-byte ZIPs, SHA-256
`1643a804c5556cf4425c2c9ea6a2c0e6ab25d87043cd5d5ca602095abd391b29`.
On this Linux host the first seed build took 0.449 seconds, first extraction
0.407 seconds and a repeated build plus extraction 0.069 seconds with all 169
files reused and both hash/copy operations forbidden by controls. These are seed
costs, not full update/Windows performance estimates. 121,059,670 generated test
bytes were removed after the final run.

This lane establishes portable compiler input delivery and targeted host repair.
It does not establish the full integrated device-only update or install path.
The native code/metadata pair still needs its existing compilation/ABI/signing
checks. Standalone self-update cannot reuse a PC BepInEx DLL as an Android native
player; evaluating automatic update UX is separate from this seed mechanism.

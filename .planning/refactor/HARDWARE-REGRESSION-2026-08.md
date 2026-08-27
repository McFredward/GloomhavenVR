# Hardware regression pass — 2026-08 refactor

> **This list is short, and its shortness is the deliverable.** Across the whole refactor exactly
> **one** type's compiled form changed, and it is item 1. Everything else — 251 moved files, ~700
> edited comment lines, five new parts of `EnvSound`, four new checkers, the build settings — is
> provably byte-identical in the assembly, verified commit by commit with
> `scripts/refactor-guard.sh check`. Testing any of it tests nothing.

## 0. Before you start

Install as usual and check the startup line names the commit you expect — a stale DLL is the most
common way a "regression" turns out not to be one:

```
[Core] v0.1.1 build <hash> [dev] (built …) loaded — N modules initialized
```

Then confirm the gate was green on the build you are running:

```
0 errors, 0 warnings · wire 151307 · mirrors 18 · frame order 9 · patch inventory 80/132
partial order 20/135/0 · instrument writes 66 load-bearing / 279 instrument-only
surface 385 config keys / 122 patches / 2 754 log tokens · bundle 70 204 340 bytes
```

If any of those disagrees, stop: the build is not the one this list describes.

---

## 1. The one behaviour change — the remote pick banner

**What changed.** `Net/Remote/RemotePickBanner` dereferenced its `_root` transform unguarded on a
line whose two neighbours both guard it. `_root` is never null in the C# sense (it is `readonly`
and set in the constructor) but it is a *Unity* object, so those `!= null` tests are
**destroyed-object** tests. On a torn-down board subtree the log line threw
`MissingReferenceException` out of a **diagnostic**, and a throw there amputates the rest of the
caller's chain. It is now guarded, and the destroyed state is reported instead of thrown.

**On a live board nothing differs at all** — same placement, same text, same log line.

**What to test.** In multiplayer, watch a teammate's pick banner (the "Keine Handkarten" /
pick-status placard on their board) through a board rebuild — a board style switch, a scenario
exit, or a peer leaving and rejoining.

**What a failure would look like.** The banner text stops updating on a peer's board, or a new
`Remote pick banner: BANNER ROOT DESTROYED` warning appears in the log during normal play (it
should appear only around a genuine teardown, if at all).

---

## 2. Nothing else in this list is a code change, and here is why each is still worth one look

### 2a. `EnvSound` is now five files — listen to one room

Pure motion, guard empty. The reason it is here at all: the bed factory's decorrelation offset
reads `Beds.Count` **at construction**, so the ORDER of bed construction decides every bed's start
offset inside a *shared* noise buffer. Get that wrong and the flame, the draught and the leaves
sum coherently — about +10 dB instead of +5 — and collapse into one audible source coming from
three places at once. The split reorders nothing and the empty guard diff proves it, but this is
the one place where a mistake would be *audible* rather than visible.

**What to test.** Stand in the cellar and in the night forest for ~30 seconds each with
environment sounds on. It should sound exactly as it did in ModBuild 297: three separable fires,
a draught that is not the same sound as the leaves, nothing "phasing" or doubled.

### 2b. 251 files moved — check the mod still loads at all

A file move cannot change behaviour, but it can change what MSBuild compiles. The startup line in
§0 is the whole test: if the plugin loads and reports its module count, every file made it into
the assembly.

### 2c. Comments — nothing to test

~700 comment lines were edited (English renderings beside German user quotes, malformed XML
repaired, dangling documentation references resolved). Comments do not reach the assembly, and the
guard is deliberately blind to them again (`refactor-guard.sh` hides the XML doc file from the
decompiler for exactly this reason). There is nothing here a headset can see.

### 2d. Build settings — nothing to test at runtime

`TreatWarningsAsErrors`, XML doc generation, and the diagnostic selection change what the compiler
*refuses*, not what it emits. The evidence is that the gate reports 0 warnings and the guard
reports 0 changed types.

---

## 3. What was NOT touched, so a defect there is not from this refactor

Tuning values, frame timing, the wire format, Harmony patch targets, the asset bundle, every
shipped default, and every `Defaults/` constant. The gate numbers in §0 are what proves it: the
config-key census, the patch inventory, the frame-order lock, the mirrored constants, the wire
vectors and the bundle bytes are all unmoved.

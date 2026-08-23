# libs/RefAsm — reference assemblies (committed on purpose)

**These files contain NO executable game code.**

They are *reference assemblies*: the type and member **metadata** of the game's
managed assemblies — names, signatures, field layout, attributes — with **every
method body removed**. A C# compiler needs exactly that and nothing more. They
cannot be run: the CLR refuses them with
`BadImageFormatException: Cannot load a reference assembly for execution`.

## Why they exist

The mod compiles against the game's own assemblies (`GH.Runtime.dll` and friends).
Those are the publisher's binaries — they are never committed and they do not exist
on a GitHub-hosted runner, so without these stubs CI could not compile the mod at
all. Committing them was chosen over a self-hosted runner.

`Directory.Build.props` resolves `$(GameManaged)` first and only falls back here when
that folder does **not** contain `GH.Runtime.dll`. A developer with the game
installed always compiles against the real assemblies and never touches this
directory. When the fallback does fire it prints a high-importance message into the
build log, so nobody compiles against stale stubs without seeing it.

## Which assemblies, and why these

The list is **derived from the projects**, not from the Managed folder: every
`HintPath="$(GameManaged)\…"` in `src/**`, `tools/**`, `tests/**` and
`Directory.Build.props`. Adding a reference to a csproj and re-running the generator
is all it takes; the generator empties this directory first, so a reference that is
dropped from a csproj does not leave an orphan stub behind.

`sources.json` records, per assembly, the SHA256 and byte size of the real DLL it was
cut from — so "were these regenerated after the game update?" is answerable without
the game install.

## Regenerating

One-time tool install:

```bash
dotnet tool install -g JetBrains.Refasmer.CliTool
```

(The NuGet package `JetBrains.Refasmer` is the *library* and ships no command; the
CLI is `JetBrains.Refasmer.CliTool`, and its command is `refasmer`.)

Then, from the repo root:

```bash
scripts/make-refasm.sh                       # uses ./ressources/Managed or Directory.Build.props.user
scripts/make-refasm.sh /path/to/Gloomhaven_Data/Managed
```

The script refuses to run without `refasmer` rather than silently copying full DLLs,
verifies its own output, and prints exactly what it produced. The output is
deterministic — running it twice on the same input gives byte-identical files, so a
no-op regeneration produces an empty diff.

**Regenerate after every game update.** A game patch changes the signatures the mod
compiles against. Stale stubs make CI green while the real build is broken — or the
reverse, which is worse, because then CI is red for a defect that does not exist.

## The check that keeps the promise

`scripts/check-refasm.py` walks each file's PE header → CLI header → `#~` metadata
stream → `MethodDef` table and asserts that **every** method's IL RVA is `0`
(ECMA-335 II.22.26). One non-zero RVA means a full assembly slipped in. It runs at
the end of `make-refasm.sh` and again in CI on every push, so the "no game code in
this repository" promise is enforced mechanically and not by good intentions.

It is not a size check. Size proves nothing on a runner that has no original to
compare against.

## Visibility: why `--all` and not "public API only"

The mod publicizes the game assemblies (`BepInEx.AssemblyPublicizer`) and calls
internal and private members directly. A public-API-only reference assembly
(`refasmer -p` / `-i`) would drop exactly those members and the build would fail with
hundreds of CS0117/CS1061. `--all` keeps every member's *metadata* regardless of
visibility. It still emits no method bodies — visibility and executability are
independent things.

## What these CANNOT do: the wire tests

`tests/GloomhavenVR.WireTests` **loads** `UnityEngine.CoreModule.dll` at runtime
(`Private="true"`) because the golden vectors depend on the real `Mathf.RoundToInt`
banker's rounding — `RoundToInt(0.5f) == 0`, `RoundToInt(1.5f) == 2` — which sits
directly on the quantization path. A reference assembly cannot be executed, so:

* the wire test project **compiles** in CI against the stub here (which still catches
  a moved/renamed wire file, the failure that project list was written to catch), but
* the vectors themselves **do not run** in CI. They run on a developer machine with
  the game installed, via `scripts/wire-tests.sh`.

The NuGet `UnityEngine.Modules 2021.3.5` package does not help: its
`UnityEngine.CoreModule.dll` is itself a stub — calling `Mathf.RoundToInt` through it
throws `NullReferenceException`. Verified, not assumed.

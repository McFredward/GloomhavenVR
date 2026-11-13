# Original shader include filenames on Windows

`tools/quest-shaders/produce.py` previously named each bound HLSL include with
both complete DXBC and interface SHA-256 strings separated by a hyphen. The leaf
was 134 characters and its asset-relative path was 178 characters. That relative
path alone does not exceed `MAX_PATH` at `C:\`. The actual generated project,
64-character input key, cache/overlay directories and Unity `.meta` suffix also
count toward the full path.

The producer now names an include with a full SHA-256 of the unambiguous,
domain-separated binary DXBC/interface identity pair. It truncates neither input
identity and does not use traversal-dependent numbering. The leaf is 69
characters and its asset-relative path is 113 characters. Duplicate pairs share
one file; a conflicting digest/path fails before writing the overlay. The
manifest records `programPathScheme=sha256-dxbc-interface-pair-v1` and retains
both complete original hashes.

HLSL contents and native instruction/interface proofs are unchanged. ShaderLab
changes only the necessary `#include` path operands. Original Shader names,
GUID/meta bytes, material references, pass states and alias/keyword graphs are
retained. This naming change belongs to a new legitimate Builder input; it must
not rewrite a running/frozen project or relabel an older native validation receipt.

On Windows, `restore_project()` checks every generated include and Shader path
under both the actual project root and private output-overlay root, including
their `.meta` filenames, before creating/writing the overlay. It rejects invalid
Win32/reserved components, components exceeding 255 UTF-16 units, and full paths
of 260 or more units. Failure gives short build/cache root guidance such as
`C:\q`; long nested Downloads/AppData paths may still fail. This check does not
assume Python's or Windows' long-path support also applies to the pinned Unity
Editor/compiler. The central Wizard must select/check its cache root early;
native recovery already uses that cache before calling the overlay producer.
See Microsoft's [path limits](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation)
and [filename conventions](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file).

The read-only actual Campaign census used manifest
`36f7c1cb1882ff458693d4c06d0b4a0e185a6972af8bc560220d090b388e595f`:
11,656 exact program identities map to 11,656 distinct case-insensitive short
paths. All 688 Shader sources permit exactly 38,604 include-operand replacements
with every other source character unchanged. The original 51,564 aliases and
9,187 material records are unaffected. No project/native output was changed and
no shader compilation was repeated. The private evidence is
`/home/claw/quest3-local/full-shader-validation/windows-paths-v1/census.json`.

| Actual generated layout rooted at `C:\q` | Old include file / meta | New include meta | New maximum Shader/include meta |
| --- | --- | --- | --- |
| `projects/<64-hex>/` | 257 / 262 units | 197 units | 211 units |
| `tool-cache/campaign-shaders/<64-hex>/overlay/` | 284 / 289 units | 224 units | 238 units |

The realistic Wizard project root
`C:/Users/McFredward/.ghvrq/build/projects/<64-hex>` gives 225 units for the new
include meta and 239 for the longest original Shader meta. Its unshortened
`tool-cache/campaign-shaders/<64-hex>/overlay` gives 252 and 266 respectively:
the include fits, but the existing longest Shader filename still fails. Short
cache directories such as `cs/<64-hex>/o` can address that central layout issue
without renaming original Shader assets. Longer user profiles can exceed the
budget even with short include names and must receive the same actionable error.

Run only the focused filename and related producer controls for this change:

```sh
python3 -m unittest discover -s tests/quest-shaders -p test_program_paths.py
python3 -m unittest discover -s tests/quest-shaders -p test_reconstruction.py
```

Eight filename tests cover stable identities, planted collisions, actual layout
budgets, Unicode/component/reserved-name boundaries, include-only ShaderLab
changes, a bounded actual overlay with unchanged program/meta/material identity,
and rejection before overlay creation. Recovery seams in that overlay fixture
isolate filename production; they do not establish new GPU/headset evidence.
The 24 existing reconstruction tests also pass. No Unity launch or complete
native compiler matrix is needed for these filename-only changes.

# Fragment stereo declaration repair provenance

The optional `campaignFragmentStereoInputOrderRepair` build provenance entry
recognizes `QuestCampaignEvidence/fragment-stereo-input-order.json` only for the
full Campaign target. The current `campaign-shaders.json` must carry the exact
noncyclic repair marker and its byte hash must equal the receipt's after hash.
A missing applied receipt or an unfinished transaction is rejected.

The recognized scope is the 90 original Foliage fragment programs whose native
input signatures contain `SV_IsFrontFace`. Capture checks their original DXBC
and interface identities, source and meta hashes, and the unchanged program
bytes after removing the one generated stereo macro. The source/meta ledger
also binds every unchanged header, Shader and Material to the current files and
native GUIDs. Compiler input, compiler witness and native driver witness are
bound by their actual file hashes and explicit cross-references.

Both the actual host `produce.py` and `stereo_repair.py` hashes are recorded,
with truthful comparisons to the frozen runtime snapshot. A missing frozen
helper has a null baseline; it never becomes a fabricated snapshot source.
The existing capture recheck rejects edits, deletions, links and membership
changes during capture. A later unequal capture changes the derivative build
identity or rejects the completed build through the caller's existing guard.

Only relative paths, hashes, counts and recognized claims are exported. Native
shader bodies, signatures, private compiler inputs and local paths are omitted.
Headset picture and original pixel parity flags remain false.

Validation: 37 focused provenance checks, including malformed/missing evidence,
changed source/meta/GUID, incomplete transactions and concurrent edits. A
read-only check against the shader worker's actual private corrected project
verified 90 repaired headers, 11,566 unchanged headers, 688 Shaders, 9,187
Materials and 51,564 original aliases against 43,073 file records. The runtime
snapshot remained `fa6c1f9a`; no Root project or game source was changed by that
check. This is input provenance evidence, not final Player or headset evidence.

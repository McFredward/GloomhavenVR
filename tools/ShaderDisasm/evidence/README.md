# Local shader evidence

Generated shader disassembly and extraction reports are local research outputs, not source files
for the mod. Generate them from your own game installation with `tools/ShaderDisasm`; keep outputs
here (ignored) or under `.planning/debug/`. Do not commit shader programs or game asset dumps.

Historical source comments name the reports that established the stereo/occlusion findings.
[../FINDINGS.md](../FINDINGS.md) retains the analysis and reproduction procedure without bundling
full extracted programs. Removing these outputs from the current tree does not remove copies from
older Git commits; repository publication must account for that history separately.

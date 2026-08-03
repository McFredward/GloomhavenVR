// GENERATED-AND-HAND-EDITABLE — the single home of every shipped config default.
//
// WHY THIS FILE EXISTS
// --------------------
// The mod binds ~550 config entries across 18 module config classes. Their default values
// were literals at the Bind call, which meant re-basing the shipped defaults onto a tuned
// setup was a hunt through the whole codebase. They all live here now, one per line, each
// tagged with the config identity it feeds:
//
//     internal const float TrayForward = 0.77584f;   // => [Cards] TrayForward
//
// The `// => [Section] Key` annotation is the machine-readable part: scripts/rebase-defaults.py
// finds an entry by it, so a line may move but its annotation must stay exact.
//
// RULES
//   * one entry per line, initialiser a LITERAL (or a `new Vector3(...)`/`new Color(...)` of
//     literals) — never an expression that reads another entry;
//   * `const` wherever C# allows it, so the compiler inlines it and the compiled form is
//     identical to the old literal-at-the-bind;
//   * `static readonly` only for Vector2/Vector3/Color, which cannot be const;
//   * the *_ByBoard / *_ByStyle arrays exist so a per-variant bind inside a loop can index
//     them; they are assembled from the named entries above them and hold no literals.
//
// Editing: change the number, rebuild. Or drop a tuned cfg into .planning/debug/default/ and
// run `python3 scripts/rebase-defaults.py apply`.


using UnityEngine;
using GloomhavenVR.Net;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Net/NetModule.cs ----------------------------------------------------------
    internal const bool Net_Enabled = true;                                            // => [Net] Enabled
    internal const int MaskId = 0;                                                     // => [Net] MaskId
    internal const float MaskSize = 1f;                                                // => [Net] MaskSize
    internal const bool NameTags = true;                                               // => [Net] NameTags
    internal const bool MirrorEnabled = false;                                         // => [Net] MirrorEnabled
    internal const bool Net_VersionGuard = true;                                       // => [Net] VersionGuard
    internal const RemoteBoardVisibility RemoteBoards = RemoteBoardVisibility.Always;  // => [Net] RemoteBoards
}

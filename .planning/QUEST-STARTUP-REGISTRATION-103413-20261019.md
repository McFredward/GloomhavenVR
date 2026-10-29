# Quest B711 startup registration repair — 2026-10-19

## Hardware evidence and cause

`quest-capture-20261019T103413Z-4be85ee4.zip` confirms installed B710 through
the package, current process banners and matching installed APK hash
`c0961c4792a7e39a0f54d8a6296f05fe8f9fd9f61de56e1f5bd9219a970ba5c5`.
The current startup state is failed before original bootstrap, with:

```text
Original module initialization: WorldUI: NotSupportedException:
No compiled Quest hooks for GloomhavenVR.WorldUI.NativeVideoMoviePatch
```

The B710 movie boundary correctly qualified and applied both native URL setter
substitutions before AOT, then removed the desktop transpilers and their patch
attributes. `WorldUIModule.Init` still calls `PatchAll` for those exact classes.
The registry had no executable hooks for the now-empty classes and rejected
them, blocking original startup. Earlier IL/callback tests checked the setter
rewrites but missed this later registration. A new regression reproduces the
same exception from the actually emitted B710 mod assembly.

## Narrow correction

After qualifying both complete movie contracts, the producer records each
static patch class, original target and removed transpiler key. The Weaver
emits those records in `QuestWeaver.StaticPatches.v1`. Registry loading validates
the complete metadata before publication, requires the reviewed empty static
class shape, rejects duplicate/conflicting/malformed entries, and preserves
ordinary owner tracking. Static acknowledgments create no executable hook slot
and do not enter runtime prefix/postfix/finalizer ordering. Normal registrations
and unknown class/selector failures retain their existing behavior.

Final export independently rechecks the live registration closure and each
qualified original target, replacement call and captured full body fingerprint.
Missing proof or late target/substitution drift cannot inherit an earlier
successful audit. These are two small method checks, not asset revalidation.

The build audit now also checks exact live `PatchAll(typeof(...))` callsites.
Known classes without executable hooks or qualified static proof become
`PATCH_CLASS_REGISTRATION_UNRESOLVED` before handoff. A literal side-effect-free
`Prepare() => false` remains a valid deliberate decline. Other runtime Type
producers retain their existing closure checks; the build host never executes
the mod or guesses Unity state to resolve them.

The other existing static boundaries were reviewed: their replacement install
methods remove the corresponding registration calls. Only the two movie classes
need these acknowledgments. The original URL assignment sites, decoder clocks,
native Play/callback ordering and exception regions remain unchanged by B711.

## Minimal update and remaining hardware check

Use the updated Builder's **Mod Update** against the retained B710 APK/workspace.
The changed managed code and AOT registry require a matched native-code/metadata
update. Original Campaign conversion, imported compatible objects, independent
movie/audio/figure/environment results and numeric movie depth recipes are
unchanged. No new depth inference or complete game-asset rebuild is required.
The delivered source manifest is compared with B710 to verify that scope.

Evidence is under
`/home/claw/quest3-local/build/evidence/quest711-startup-20261019/`.
It includes the red B710 startup reproduction, emitted B711 registry activation,
closed-callsite coverage and negative controls, preserved original movie IL,
strict builds, focused source checks, actual original-assembly emission and the
extracted public Builder check. Unchanged game/network/UI evidence remains
inherited; no new complete local gate or full local APK build is claimed.

The focused Weaver suite passes 878 assertions. After the final writer-only
guard, 27 registry controls and 814 assertions against the actual B711 mod pass,
including all 218 fixed registrations, 196 direct runtime activations, both
static movie proofs and three damaged/missing/rebound export negatives. Normal
hook behavior reuses its already passing focused-suite evidence. Actual root
emission closes 488 executable hooks on 301 native targets without issues.
Strict mod and Weaver builds have zero warnings/errors; all 16 source suites
pass. The emitted assembly and executable startup test use the same B711 DLL
hash. Final archive/source comparison and extracted delivery results accompany
the public handoff validation.

Actual Intro/menu playback and continued scene loading on the Quest remain
hardware checks. Passing the host registry regression closes the reproduced
software exception, not the headset's final picture or every subsequent gate.

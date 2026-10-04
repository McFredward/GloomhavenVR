# Complete standalone Campaign port: implementation evidence

Work continues on `feature/quest3-standalone`; the concurrent desktop `dev` tree
is independent. The requested deliverable is the full Campaign hardware package,
including owned Jaws of the Lion and Solo Scenarios, original VR controls/MR,
local native saves and original session-code multiplayer. Guildmaster and Steam
Workshop remain explicitly unavailable. Store/cloud/friends services stay absent.

## Original content and dynamic generation

The actual owned rules inventory contains 140 custom-level entries: 95 Campaign,
25 Jaws of the Lion, 17 Solo and three examples. This inventory is neither a
scenario-playthrough count nor proof of rendering. Campaign gameplay RNG remains
original. Fixed scenarios still rebuild geometry when obstacles, tile avoidance,
object membership and procedural parameters change; a pristine-scene bake cannot
preserve every original gameplay function.

The original engine is an x64 Windows native library. The isolated same-version
Unity Mono host runs that untouched library and the original 601 procedure graphs.
It executes the actual Intro input-singleton scene, original startup/rules loading,
and Scenario_Campaign_001. Its observed generation completes 18 procedural owners
and creates 1,986 renderers. The original scene controller's headless loading UI
flag remains set; the receipt exposes that limitation instead of pretending the
headless scene is playable. No gameplay RNG, original rules or placement-ready
flags are substituted.

`tools/QuestProceduralExport` copies the actual native build/asset/task traffic
before or after each original call. Scenario001 supplies 18 build requests, 87
asset requests, 92 asset responses and 18 task buffers. The native-helper worker's
independent original-DLL replay matches all frame/parameter/geometry data after
normalizing only documented opaque object/resource identity handles. This is host
native parity; the Android guest dependency closure and Quest execution remain
separate work.

All 3,255 original catalog bundles have completed bounded recovery, capturing
916,609 serialized original objects. The completed catalog association has no
unresolved locations. Exact original CAB/pathID identities retain object and
subobject ownership. Public recovery no longer needs the private B614 cache:
six retained shader GUID contracts are established through exact native object
bytes. Full hash validation, Unity import and complete shader reconstruction are
distinct gates. The source contains 688 physical Shader identities; a successful
sample compiler result cannot certify the entire set.

## Platform, save, networking and compile boundary

The game-target platform adapter preserves original rules, save serializers,
admission tokens and Photon/Bolt transport. Local profiles support Steam/Epic/GOG;
full provider IDs remain separate from the original 32-bit account field. Steam
local metadata, complete local provider DLC evidence or an explicit purchased-DLC
declaration controls entitlement. Bundled DLC bytes alone never imply ownership.

Native save writes bind only the filesystem seam to temporary-file/flush/backup
replacement. Original queue/callback/serialization behavior remains authoritative.
Quest suspension requests the original local/host save flow; it does not promise
that Android grants time for all callbacks. The save export/import scripts retain
the entire original root and back up replacements.

The ARM64 Opus library uses fixed native CTL wrappers rather than Windows varargs.
Original voice opt-in requests microphone permission before entering the original
bridge. The complete networking/storage Quest sources compile successfully with
the real Unity2021 Android player compiler. A generated QuestGame.Campaign assembly
references the original game explicitly without exposing its global Debug wrapper
to unrelated Unity packages. Compilation does not prove PC room admission, actual
microphone operation or headset rendering.

Current focused QuestWeaver proof: 358 assertions, including roundtripped native
proxy bodies and a changed-ABI negative control. Original DLL adaptation verifies
5,050 unrelated types unchanged. Full packaging remains gated by complete assets,
faithful shaders, native runtime dependencies and current-mod AOT evidence; no
menu-only APK has been relabelled as the requested complete game.

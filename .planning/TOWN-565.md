# Repeated town interaction and physical presentation follow-up — ModBuild 565

The supplied Debug `LogOutput.log` identifies ModBuild 564. It contains eleven temple
presentation sessions interleaved with merchant visits. Each temple opening is immediately
followed by the expected off-scenario hand-source release, but the log has no exception or
fallback explaining the later missing purse and empty book. No new screenshots accompanied
this round. The causes below are therefore separated into source/runtime proof and the remaining
headset verdict.

The missing temple presentation was a split session identity. `TownServicePresentation` asked
the interaction mirror about its native source session, while `TownServiceSync` deliberately
publishes a separate monotonic wire generation that also advances on service changes and
relocation. Those values can coincide in a first visit and diverge after visiting other NPCs.
The local owner then lost input visibility despite still holding the resident lease, hiding the
purse and original book inscriptions. Ownership now requires the matching source service/session
but queries the mirror with that source's current wire generation. A focused regression starts
the mirror at wire generation 7 while the native presentation is session 900; the valid visit is
accepted and stale source sessions or another service remain rejected.

Merchant and priestess attention poses were checked by rendering the actual imported prefabs,
not only their authored IK targets. Station forward points from the visitor toward the resident;
the previous targets used the opposite assumption and clamped both arms over the counter. The
new reachable targets settle at the residents' own waists with coordinated elbows and inward
palms. An unavailable temple visit interpolates from that pose to the bowl over the existing
0.70-second shared transition. Both palms now finish facing down; the previous roll signs faced
them upward. Runtime checks evaluate the transformed palms on the imported models and bound each
90 Hz transition step, so testing no longer certifies an unreachable target as a natural pose.

The shipped cloth runners contain 889–918 FBX-imported visible vertices. Unity reorders and
splits them by face and UV, so the first 325 vertices are not the assumed 25x13 source grid.
The hidden solver also inherited a 100x FBX child beneath the roughly 198x map frame and then
multiplied its freedom and collision distances by that lossy scale again. A real-bundle harness
reproduced 307.043 world units of hand displacement and 561.687 units of gravity displacement in
the old inherited-scale solver. The replacement reconstructs the validated curved 25x13 physical
surface, maps every visible solidify/embroidery vertex to it, and simulates in a unit-scale root
that follows source pose and later room-scale changes. Thirteen front-to-rear table capsules align
with the physical columns. Local probes use the same real palm-to-index-tip endpoints as figure
cloth; remote tracked directions, heads and owner-authored cloth state keep their existing paths.
Every support and probe remains on Ignore Raycast.

The recurring flat opening and closing cues originate in `UIWindow.Show(bool)` and
`UIWindow.Hide(bool)`. The former town patch was installed from presentation Tick, after the first
Show, and covered only the enchantress card-tab display. The three exact native town window types
are now patched during `WorldUIModule.Init`. Their serialized show/hide item is blank only for the
duration of an immersive call and restored by a Harmony finalizer, including exceptions. A weak
open marker covers map teardown when the room deactivates before Hide. Disabling immersive town
services or its hand path retains the complete 1.0.6 flat audio behavior. Debug records each
suppression; normal user logs do not grow.

EBU R128 measurement of the final source assets put merchant speech at -24.60 LUFS on average,
priestess speech at -26.84 LUFS and enchantress speech at -25.60 LUFS. Priestess playback receives
2.28 dB (`1.30x`) source gain. The speech/effects preference, distance rolloff, individual WAV
dynamics and quieter prayer-to-speech ratio remain unchanged. The enchantress lamp is now seated
after its copied renderer bounds and normalization are known. Its full visible footprint is
clamped to real workbench mesh support instead of grounding only the pivot with a capped inset;
the flame and body share the resulting seat.

Focused validation passed the actual Unity 2021.3.5 town bundle for all three cloth runners, with
16.30–23.37 world units of hand response and at most 5.48 units of table penetration (about
27.7 mm perceived, inside the modeled support skin). The cloth source contract and eight negative
controls passed, as did 281,159 private-workspace assertions. Native window audio passed 27
assertions, voice playback passed 2,699 assertions plus thirteen controls, and decoration passed
98 assertions on the real bundle plus fourteen controls. Shared interaction generation passed its
production case and four mutation controls. The imported activity production run passed 625,355
assertions; its complete mutation set checks palm orientation, transition continuity, anatomical
contact and network handover. Strict Release builds reported zero warnings and errors in both
worker lanes. The integrated guard passed all 14 source suites, all 79 local runtime suites and
286,569 wire assertions. Both Unity bundles rebuilt successfully; the expected compiled-form
delta contains the feature branch's 129 changed and 212 added/removed types relative to the
released-development baseline.

Automated checks establish the solver scale, transformed contact positions, audio call boundary,
source loudness and renderer support. A headset retest still decides the cloth's visible weight
and finger response, both attention silhouettes from stereo viewpoints, the smooth bowl-cover
gesture, repeated temple visits after each other NPC, perceived voice balance, complete absence
of the flat window cues and the final visible lamp seat.

# Town service hardware follow-up — ModBuild 562

The supplied local `Player.log` identifies ModBuild 561. No remote log was supplied.
The seven screenshots in `.planning/debug/npc_probleme/` show the priestess and merchant
visitor poses with raised or disconnected-looking shoulders, the temple's original dark page
strips through the parchment, ordinary ability cards in place of the temple purse on a repeat
visit, a returned merchant item with the wrong fan rotation, and practical lanterns hanging
outside their furniture or too close to an edge. These are direct hardware observations of the
previous build; the changes below still require a headset retest.

Temple approach is now a symmetric proximity latch. Leaving its 1.4 m radius always releases
the latch, including after switching to another service, so a later visit rebuilds the purse
instead of leaving the ordinary map hand visible. Donation still commits only through the
game's original confirmation callback. Merchant item cards now stay hidden and non-interactive
until their asynchronously loaded original front is ready, then emerge in their canonical home
rotation. This removes the first-frame brown/back-facing card without inventing substitute art.
The blank temple parchment covers the complete measured page islands, including the narrow
raised strips visible in the screenshot.

Merchant and priestess attention poses now author the elbow route together with the hands and
blend over 0.70 seconds. This lets the upper arm leave the shoulder downward instead of holding
an abducted upper arm while the hand reaches for the hip. The merchant's coin cycle has a much
shorter neutral seam so it does not visibly stop between transfers. The altar cloth accepts a
larger hand-contact radius and stronger bounded displacement. Native practical lanterns are
smaller and moved onto the measured furniture footprint so they do not float beside the stand
or sit on its rim.

Automatic proximity entry for the three immersive services now uses the game's toggle callback
directly, preserving its mode and tutorial lifecycle without sending the pointer events that
play the old flat-window click. Physical map buttons retain their original sound and input path.

Every resident speech event now has five prerecorded English variants: merchant greeting,
offer, purchase and sale; priestess greeting, prayer and donation; enchantress greeting, cast,
enhancement and invitation. The elected resident author chooses the variant without immediate
repetition and publishes the exact cue, generation and age through the existing shared face
record, so all peers animate and hear the same utterance. The merchant uses a deeper, clearer,
close-spoken delivery. Voice and prayer sources use Unity linear distance rolloff to 7 m;
the quieter physical coin contact keeps its shorter range. All WAV files are mono compressed
assets with baked mouth curves; no synthesis or analysis runs during play.

Focused validation passes 624,204 production motion assertions plus 39 compiled negative
controls, 1,242 merchant handoff assertions plus 11 negative controls, 899 enhancement handoff
assertions plus 19 negative controls, 178 temple transaction assertions plus 17 negative
controls, 90 native-decor assertions plus 11 negative controls, and 157 resident lifecycle
assertions plus 10 negative controls. The strict Release build is clean. Final integration,
wire, guard and Windows bundle results are recorded after the bundle rebuild below.

Automated checks cannot establish perceived voice quality, arm naturalness, cloth strength,
lamp placement or the complete repeat-donation flow in a headset. Those remain the required
hardware judgments for ModBuild 562.

Final integration validation: all 14 source suites passed. The full parallel runtime run passed
76 unaffected suites and the production half of the voice suite; that suite exposed an obsolete
negative-control mutation, which was corrected and then passed independently with 2,832
production assertions and 12 negative controls. This gives 77/77 current runtime suites without
rerunning unrelated successful suites. The byte-exact multiplayer harness passed 286,560
assertions. Strict Release compiled with zero warnings and errors, and the bilingual docs check
passed. Unity 2021.3.5f1 rebuilt the Windows town bundle; the UnityFS gate accepts it at
96,853,049 bytes with SHA-256
`881a96fc261f5608b7d021cdbc819bb25959910f987d0865a261743f1439cd00`, below GitHub's
100 MiB object limit. The private refactor baseline is intentionally older than this feature
branch, so its final compiled-form comparison remains an expected source-difference report.

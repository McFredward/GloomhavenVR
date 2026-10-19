# Semantic hover haptics runtime proof

Run `python3 scripts/check-haptics675-runtime.py` from a checkout with the original
publisher managed assemblies and Unity 2021.3.5f1. This is a local native suite.

The fixture executes the unchanged production encounter gate, hand sender,
fingertip fan method, VRCard poke entry, ray UI handoff, geometric half feedback
block and board hover toggle. Original FullAbilityCard, FullAbilityCardAction,
FullCardEventPusher, Button and Unity collider objects provide native identities
and child hierarchies. Card components are created disabled on an inactive root;
the native full-game visual callbacks do not belong to this input-feedback proof.

The clock, device impulse delivery and candidate ownership are explicit fixture
boundaries. Assertions count requested presets; there is no physical controller,
HMD, rendered-game or subjective vibration-strength claim. Production and fixture
hashes, native assembly hashes, each compiled causal mutation, build logs, Unity
logs and the result remain under `.planning/debug/haptics675-runtime/`. The shared
`cleanup_completed_run` API removes generated project caches after each run.

Cases cover delayed fingertip/palm/poke drivers on a card, fast changes between
different cards including the same frame, separate hands, brief loss and genuine
reentry (the same 150 ms grace applies to cards and action regions), simultaneous
continuous action producers without a global winner ping-pong, native icon/tooltip churn within a half, top/bottom/default transitions,
a default pusher outside the button subtree, delayed physical contact arming,
legacy callback deduplication, click/grab confirmation, silent far board hover and
preserved near board feedback/configuration. Eight mutations must reach their
specific runtime failures; compilation or native-loader failures never count.

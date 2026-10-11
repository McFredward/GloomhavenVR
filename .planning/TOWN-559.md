# Town service hardware follow-up — ModBuild 559

Build 558 evidence: `.planning/debug/LogOutput.log`, `Player.log`, and the seven
screenshots in `.planning/debug/npc_probleme/` dated 2026-09-25. The logs show a
Temple visit opening, closing, and releasing the off-scenario hand source. They
do not record a completed donation or a host currency result, so they cannot
establish whether the purse reached the native callback. The next Debug test
records one bounded purse-release decision, including pose, trigger, eligibility,
identity, and bowl inclusion. A separate normal-level marker records execution
of the original native donation callback; that marker is not a host settlement.

The bag template is rooted at its visible base. The held grip now seats its neck
at the pinch, the palm presentation clears the fingers, and entering the bowl
gives one cooldown-limited haptic pulse. The priestess approach no longer latches
while another native destination is active. This resolves the source path by
which a merchant visit could leave the player near the priestess without opening
her interaction until walking away and returning. Merchant handoff, gaze, and
nearby-visitor checks use fixed distances regardless of previous interaction.

The original temple book remains in place. A curved, unprinted parchment skin
rests immediately over its dark decorative print so native localized text has a
readable backing. The merchant coin path no longer pauses with a coin held in
midair. Resident AudioSource distances now use the current station world scale;
the old 4.5-world-unit cutoff corresponded to roughly 2.3 cm in the logged map
at 198.12 world units per perceived metre.

The merchant crank has irregular forged and worn details. Two temple runners and
one enchantress runner now cross the stone edge without entering it. Their pinned
top and moving lower edge use bounded spring contact from local and remote heads
and hands. The elected resident author sends three edge-control pairs through an
optional 24-byte tail of TLV79. The original 115-byte resident prefix remains
unchanged, and receivers lacking the tail use stationary controls plus the same
wind phase. Full worst-case presence occupies 7673 of 7680 reassembly bytes;
the 7930-byte local buffer retains the prior 257-byte margin. An additional
visitor at the priestess or enchantress sees a separate native furniture clone.
Its owner sends one or two quantized cloth runners in additive TLV90 on the
existing private furniture module, at most 15 Hz for cloth-only changes. The
observer replays the controls between packets and retires the cloth collider
when the furniture hides. Public and private cloth therefore share the same
owner-authored contact state without enlarging the presence packet.

The costume's prior hand-shell cut also removed inner-sleeve geometry, exposing
flat arm ends at close range. The fitted sleeve lining follows the original wrist
and forearm bones and preserves the current face, hand, and skin assets.

The first Windows bundle rebuild exposed two obsolete `AltarCloth` prefab
objects whose FBX mesh IDs disappeared when the new runners replaced them.
They were removed. The three replacement `ClothRunner` subtrees had not been
instantiated in the furniture prefabs either; both priestess runners and the
enchantress runner are now present with their decorative trim. Every remaining
town prefab MeshFilter resolves a real mesh. The town bundle build fails on a
missing mesh or an incorrect runner count so the shipped prefab cannot silently
lose its animated cloth again.
The extra enchantress ornament on the right is included in the measured
visitor-furniture clearance envelope.

Automated gates verify source-bound interaction, resident authority, TLV79/TLV90
golden vectors and truncation, furniture geometry, bundle contents, and strict
Release compilation. The final pass recorded 14/14 source checks, 76/76 suites,
286,501 wire assertions, and a zero-warning Release build. The compiled-form
guard reports the expected broad difference from its older dev baseline because
the NPC feature branch contains many previously added production types; it
reported no source, wire, bundle or surface-gate failure. These gates do not
establish headset appearance, sound level,
touch feel, or a completed multiplayer donation; the next hardware test must
check those.

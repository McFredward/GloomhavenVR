# Town stand fabric retirement — Build 582

The maintainer reports that the priestess and enchantress stand fabrics still do not
respond to a hand in the headset. This has persisted across several revisions despite
positive native Unity fixtures. The stand fabric remains visible as authored static
mesh and material. This retirement removes its separate simulation and touch path;
figure clothing physics is a different system and remains untouched.

## What was tried

- Build 562 used a hand-written edge spring. It moved too few points, could not
  fold around a hand, and let fabric pass through the workbench.
- Builds 569–571 added a hidden Unity `Cloth` driver under the rendered mesh,
  tapered hand colliders, table supports, and immediate presentation of PhysX
  displacement. A 120 ms visual fade had concealed short touches, so it was
  removed. A first-contact baseline reset had erased fast taps; it was changed.
- Build 572 added a merchant side drape and synchronized its control points.
  Build 573 removed that drape and runner after the cabinet changed.
- Build 574 addressed Unity's `All cloth particles are fixed` refusal to
  initialize by allowing sub-millimetre idle freedom.
- Builds 575–580 retuned travel/damping and contact onset, extended probing
  from fingertip to wrist, sampled triangle interiors, added a continuous
  clearance constraint, stabilized hand identities, and corrected contact
  gating against the moving surface. Owner control points and private workspace
  cloth used additive resident/TLV90 presentation records for observers.

The Unity fixtures measured centimetres of displacement and rejected no-contact
mutations, but the headset video in `TOWN-580.md` shows a hand crossing a nearly
stationary drape. The gap is between the native test setup and the rendered VR
experience; a positive fixture is not proof of headset motion. The exact remaining
runtime cause has not been established. Further solver changes without a verified
headset-visible measurement would repeat the same unproductive loop.

## Retired behavior and compatibility

No town station or duplicate workspace constructs a Unity `Cloth`, hand/head
probe, support collider, dynamic vertex writer, or contact-tick path. The
resident author publishes only the original pose record; furniture modules
publish ordinary static mesh/material snapshots. The historic cloth record
IDs and decoders remain reserved/readable so persisted protocol grammar and
old packet vectors cannot silently change meaning. New Build 582 senders do
not author fabric control records, and the mirror does not use legacy controls
to start a fabric simulation. In particular, static fabric has no collider
that can block the laser. The visible authored mesh and existing figure garment
physics remain.

This is an intentional temporary removal at the maintainer's request. A future
reintroduction needs a test that captures the *actual rendered headset surface*
while a tracked hand presses it, including first contact, sustained drag,
release, and the observer view. Unity-only solver displacement is insufficient.

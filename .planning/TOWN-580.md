# NPC partial hardware follow-up — ModBuild 580

## Build 579 evidence

The supplied local `Player.log` identifies ModBuild 579 at Debug level. No
remote log was supplied. The merchant photograph in
`.planning/debug/npc_probleme/VirtualDesktop.Android-20260928-134113.jpg`
shows both hands suspended ahead of the coat. The priestess video
`VirtualDesktop.Android-20260928-134224-0.mp4` shows an angular, twitching
prayer-to-neutral transition. The cloth video
`VirtualDesktop.Android-20260928-093808-0.mp4` shows the hand crossing a
nearly stationary side drape. Those headset observations are the acceptance
targets; coordinate-only hand markers and green source checks are not proof
of visual contact.

## Merchant speech timing

The native shop's visit range begins closer to the merchant than his gaze
range. The greeting was queued from the native visit edge, so the visitor
could already be watched without hearing him. The elected face author now
queues a greeting when shared attention first rises; the native visit no
longer queues a second merchant greeting. Trade speech retains priority,
and the existing cooldown bounds repeated greetings. The portable voice
schedule and production Unity assertions pass. An independent review found
that a client promoted from observer to face author could otherwise greet an
already-watched visitor again after the cooldown. Followers now retain the
observed gaze level as the handover baseline; a later genuine gaze edge can
still greet. The portable handover regression and 3,888 production Unity
assertions pass. A headset run is still needed to judge the perceived
distance and speech timing.

## Town cloth

The build-579 debug trace recorded the hand inside the preparation margin
while the narrow diagnostic contact test stayed false. That false contact
edge also gated *visible* deformation. The visible mesh now follows native
Cloth whenever a hand is nearby, and a continuous hand-capsule clearance
constraint protects triangle interiors between the relatively sparse
physical particles. The constraint uses the same local and remote hand
anchors as the native colliders and preserves its entry side while the hand
pushes through the thin sheet. Stable local/peer hand identities prevent a
tracking dropout or player reorder from transferring the side latch to a
different hand. A real withdrawal releases that latch before a later touch
from the opposite side, and hiding/reopening a station restores the authored
rest mesh even when left/right deformations averaged to zero. A far-hand fast
path avoids the visible-mesh work during idle.

The final executable Unity sweep at NPC scale 198 measured at least 8.66 mm
between the entire hand capsule and every visible cloth triangle during
approach, hold and withdrawal, with no residual displacement at rest.
The largest single-vertex withdrawal step was 15.0 mm per 90 Hz frame.
The headless player measured idle author ticks at 35.9 µs median / 44.4 µs
p90 and active contact at 1.25 ms median / 1.39 ms p90. The final Unity run
also passed the 34 active negative controls and separate local/peer slot,
opposite-side re-entry and hide/show checks. These are runtime geometry and
cost checks, not headset confirmation of the drape's look.

## Motion

The build-579 priestess change used separate arm clocks and opposed vertical
arcs. The headset video shows the resulting twitch and square elbow shape.
Those clocks and arcs have been removed. Her prayer release and return now
share one continuous attention phase, with a coordinated wrist/elbow route.
The old wooden-surface support correction also activated abruptly as the
hand crossed a narrow height band; the temple pose now fades that allowance
through the final part of the authored attention motion instead. The exact
candidate was rendered from the imported Unity skin for both directions;
the 90 Hz fixture passed 629,560 assertions with less than 5 degrees of
upper-arm/forearm rotation per frame, and a 585-frame skinned-mesh scan found
zero arm/arm or arm/robe triangle intersections. Headset motion remains to
be verified.

## Merchant skin acceptance

Build 579's hand target markers were not evidence of visible contact: the
photograph shows a gap between both hands and the coat. The first higher-elbow
replacement likewise passed marker-based assertions while its actual palmar
skin remained a median 9.0 mm (left) and 7.79 mm (right) away, with only
31%/37% of its palmar vertices within 5 mm of the coat. A second unsigned
distance metric mistakenly counted vertices *inside* the coat as contact.
The new Blender/Unity fixture exports the imported skinned surface over
attention, withdrawal, offering, coin-author handover, and the 64-second Idle
cycle, then measures signed exterior contact, skin intersections, and finger
clearance. A 15 mm skin-only hover mutation confirms that hand markers cannot
make a failed surface appear to pass. It also checks both legally reachable
work phases and the animation between them. No merchant geometry candidate has
yet passed that full gate; the asset remains under revision. Build 580 retains
the previous merchant hand pose so the verified speech, priestess and cloth
changes can be tested without introducing the candidate's visible intersections.

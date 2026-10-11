# Animation service evaluation — 2026-09-21

The maintainer requested realistic work loops and smooth interruption when a visitor
approaches. Existing FAL credentials remain private; this evaluation made no paid calls.

## Available APIs

- [FAL Hunyuan Motion](https://fal.ai/models/fal-ai/hunyuan-motion): text-conditioned
  human motion with FBX output, currently USD 0.08 per generation. The existing FAL
  credential is sufficient for the endpoint. This does not establish finger fidelity,
  prop contact, loop continuity or retargeting quality on our existing skeleton.
- [FAL Meshy multi-animation](https://fal.ai/models/fal-ai/meshy/rigging/multi-animation):
  automatic humanoid rigging and selection from an animation library, USD 0.20 per
  request plus USD 0.12 per chosen clip (three clips: USD 0.56). These are presets,
  not an arbitrary prose description. Replacing our completed facial rig merely to
  obtain a body clip would be an unnecessary regression risk.
- [Meshy direct Text to Motion](https://docs.meshy.ai/en/api/text-to-motion): arbitrary
  motion prompts, 2–10 seconds in half-second steps; Prime returns FBX for 10 credits,
  Swift BVH for three credits. [API pricing](https://docs.meshy.ai/en/api/pricing)
  describes prepaid API usage. This is separate from web-app subscriptions and would
  require a Meshy API credential. No dollar conversion is assumed without the actual
  credit purchase quote.

Prices and capabilities were read from the linked vendor documentation on this date.
No generated animation has been evaluated or included in build 544. Vendor quality
claims are not evidence about these three NPCs. The upstream Hunyuan Motion community
license has territorial restrictions; FAL's model page advertises commercial use, but
this evaluation does not establish the provider's terms for every deployment.

## Implementation choice

The work loops retain the existing body rig and use deterministic arm contact targets
with analytic interruption/resume. The merchant holds a native coin and a reed pen,
writing in the original open book; the priestess prays; the enchantress studies the
same native book and raises a hand with an original candle-glow mesh as a restrained
spell effect. The visible practical lighting and original decoration remain intact.

A future generated or captured body clip can replace the broad torso/arm performance
without discarding the facial rig, shared activity state or final contact correction.
Precise book, tool and finger contact still needs authored constraints. Current render
fixtures and hardware review, not a service name, determine whether the result is usable.

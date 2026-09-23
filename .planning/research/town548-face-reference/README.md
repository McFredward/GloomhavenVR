# Merchant identity reference, build 548

`merchant.png` is a new three-view reference generated with the built-in image
model on 2026-09-24, using only the original game's `merchant-original.png` as
its identity input. The tool does not expose a model selector; no claim is made
that a particular GPT image model produced it. No FAL API call was used.
The original game portrait remains the identity authority. This sheet is an
albedo/angle aid, not evidence that the final skinned face resembles that portrait.

Prompt: create equal front, left-profile (nose right) and back head panels on
neutral grey, at consistent scale; realistic diffuse skin for texture projection;
retain the original middle-aged bald merchant's broad nose, expressive angled
brows, warm brown eyes, compact full dark-brown beard with grey at the sides,
rounded dome and friendly closed resting smile. Avoid the earlier elderly,
gaunt neutral face, long narrow goatee, horizontally stretched features, heavy
baked shadows, text and clothing. Use fine pores and beard strands, not a new
character design.

Preparation splits the 2048 x 768 sheet at integer thirds; each panel is scaled
proportionally to 718 pixels high with Lanczos sampling and centred on a 718 x
718 grey canvas. The three outputs are `front.png`, `left.png`, `back.png` for
`author-town-facial-topology.py --references`. Source pixels are not otherwise
retouched. Measured source landmarks are stored in the authoring profile;
nostril and lid registration use UV coordinates on the actual anatomical loops.

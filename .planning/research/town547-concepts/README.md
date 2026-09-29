# Town furniture concepts — build 547

Generated with the built-in image tool, one image per station. The tool does not expose a model selector. No FAL requests were used for these concepts. Original output PNGs are retained beside this document.

## Shared prompt

Use case: stylized-concept. Asset type: production concept for detailed realistic medieval fantasy VR furniture, Gloomhaven Digital mod. Render a buildable freestanding piece of furniture alone on neutral warm gray studio background, three-quarter view plus a smaller top view inset showing exact practical footprint. No people, no text or labels, no interface widgets, no landscape, no modern electronics, no drawers. Handmade irregular worn edges, visible joints and thickness, believable grain and stone, carved details and aged hardware, realistic proportions; not simple rectangular blocks. This will be rebuilt as real meshes for close VR inspection. Existing game candles, books and coins will dress the finished model so leave functional surfaces clear.

## Station direction

- Merchant: generous wraparound open sales counter, shallow horseshoe with open sloped tiers for all exposed cards; worn carved oak, leather transaction mats, clear ledger/coin workspace and a supported lantern.
- Enchantress: asymmetric rootwood workbench, engraved round slate inset, curved legs and braces, purple cloth, grimoire, candle sconces, brass tools/crystals and unobstructed handoff space.
- Priestess: weathered curved stone mensa, carved arch support and oak details, burgundy cloth, bowl/prayer book/wax candles and a clear offering area.

## Runtime adaptation

The concept is an art reference, not a render of the shipped asset. Authored meshes follow explicit card terraces and hand-contact measurements; original game props are added at runtime. Source: `scripts/author-town-furniture.py`. Re-run Blender to generate four FBXs (three stands and the merchant open return), then `TownServicesBuilder.RefreshPresentation` to bind the original materials. No generated gameplay widgets, currency or native assets are baked into the furniture.

# Original town publication and cold assets, Build 622

This review found an additional source defect after the first shared publisher
patch: `enchant.row` prewarm and priority were applied to ritual inscriptions,
which contain temple ledger entries. The real `UINewEnhancementShopInventory`
pool is discovered by `CollectDynamic`. Its rows still passed the coarse root
rectangle visibility test. A row root can be outside a mask while its original
child graphics extend into the viewport. The corrected dynamic loop publishes
those complete original rows; the inherited native `RectMask2D` owns clipping.

The old mirror fixture could not establish this behavior. Its dynamic collector
was empty, and its observer project imported `TownNpc.shader` before resolving a
material. That warmed `Shader.Find` and never exercised the asset-bank path. Its
offered-card routing expectations also still required a pooled native widget,
and its temple assertions described a shared ghost that the latest explicit
local-guide ruling excludes.

`scripts/check-town-publisher622.py` binds the production dynamic collector,
publisher tick and partition registration, native physical-purse prewarm,
`BundleShaders`, asset resolution, material property validation, capture, codec,
clone neutralization, numeric motion and observer playback. Native game widget
construction and catalogue/model fields remain explicit fixture boundaries.
The fixture uses real Unity UI trees, native pool references and mask geometry;
it checks published partitions and reconstructed content rather than only the
publisher's recorded arguments.

The shader bank is built in a separate editor process from the four actual town
shader sources and their real includes. The observer project imports no town
shader. It requires `Shader.Find` to return null before asset resolution, loads
the bank, and proves `BundleShaders` used `AssetBundle.LoadAsset<Shader>` at the
registered path. Each shader must be supported, expose nonzero properties, and
round-trip its complete actual material property contract. A pending first
lookup must recover after the bank arrives. Linux GL compilation exercises this
resolver contract; it does not establish the shipped Windows D3D11 appearance.

The native publisher probe covers an offered face with `NativeSource == null`,
exact face/body priority and live-motion registration, all four original option
rows including roots outside the mask, row text/style/geometry, scroll/mask and
hover changes, and the original palm confirmation's content and intermediate
alpha transition. It reverses artwork arrival, delays the actual offered face,
and delivers newer motion before that face's baseline. The final observer pose
must equal the latest owner card pose in a translated and rotated observer frame.
Native gameplay controllers and button callbacks must be absent from clones.

The fixture suspends its local endpoint while replaying the remote endpoint;
otherwise two players sharing one process can compete for the same interaction
lease. It preserves the sender's registered modules and numeric baseline during
that suspension. Confirmation checks allow its production owner-sample interval
and inspect an intermediate value before checking the final state.

Separate cases verify priority for actual current/from/to cabinet page surfaces,
both physical purse addresses frozen before a first visit, and actual temple
body registration before its original inscription. The older routing fixture
also runs with the corrected actual-face and visitor-local-guide expectations.

Eight causal controls remove the pooled-source independence, original row
prewarm, native-window priority, cabinet-page priority, body-first order, cold
shader bank resolution, held-purse prewarm or late root-motion application.
Each must fail its corresponding runtime assertion. Evidence retains source
hashes, bound production sources, binaries, bank hashes/assets and diagnostics;
generated Unity caches and duplicate engine references are discarded.

This is source and automated runtime evidence. The supplied hardware evidence
remains Build 620; no Build 622 headset result is claimed.

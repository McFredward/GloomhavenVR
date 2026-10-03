# Native town artwork and root geometry proof

Run `python3 scripts/check-town-native-peer.py` from a prepared developer checkout.
The checker compiles the production town mirror/binding/material/asset code and
launches separate Unity 2021.3.5 owner and observer processes. It needs the
read-only complete `ressources/GH_Data` reference, UnityPy, the Unity editor, .NET,
and an X server (`xvfb-run` is used). `UNITYPY_PYTHON` and `UNITY_PATH` can override
the developer-machine locations. No original game data is changed or committed.
`--source-root` binds another checkout; `--no-negative-controls` runs only the
production proof. This content-dependent developer fixture is not a substitute
for the existing portable CI mirror suites.

The exporter reads the game's serialized UIInfoTools slots (level1 object 11386):
Poisoned, Wounded and Target. It also extracts the original
`T_rect_frame_mask_card_wide` texture. Every source object/file is identified and
hashed in the generated `native/provenance.json`. Owner and observer independently
pack the same native texels into deliberately different BC1/BC3-named atlases and
borrow original card templates in opposite orders. The owner writes actual
`TownServiceCodec` snapshots; the observer starts with its own registry and native
originals and cannot access owner Unity objects. Semantic serialized-slot identity,
later verified template aliases, baked-to-original sprite identity, original
shader/material dependencies and fail-closed ambiguous originals are exercised.

The pictures compare the observer's original native sprites before and after
production playback exactly. The original source texels must also match exactly
in both independently packed atlases. Owner-native versus observer-native raster
pictures are retained separately: different bilinear atlas-edge samples already
exist before playback. The observed 813 edge pixels are not excused playback
errors or secretly discarded pixels; observer-native versus playback changed
**zero pixels**. The center sprite at the same atlas location also matches the
owner exactly. This fixture does not claim that two independently packed Unity
atlases produce identical edge filtering.

Geometry uses the actual production `CardMesh.Build` implementation extracted
with its constants and outward-normal helper, not a cube substitute. Its rounded
114-vertex/112-triangle backing is captured, reconstructed and moved through
baseline and fast numeric updates. World pose is checked before cameras recenter.
The GL editor uses the same engine UI texture shader for both pictures because
it cannot execute Windows-only shader variants from the game's bundle; the real
production mesh remains unchanged. Visible source/observer backing pictures
contain 35,792 body pixels and differ at one raster pixel. This does not establish
the cause of the hardware screenshot's second grey/tan plane.

A separate native-mask geometry case freezes a framed root at 240×130, then
captures its actual stretched rect at 360×180 and resizes it numerically to
470×230, including independent pivots. Production receiver world pose, actual
root extent, every pinned/stretched child extent/pivot/anchor and rendered mask
are checked. The second resize must not resend original artwork. Root Transform
fields 16/17 contain actual `rect.size`; descendant fields intentionally contain
`sizeDelta` and keep their existing contract. Root numeric fields 0–9 stay
canonical so the header remains the sole world-pose author.

Seven compiled negative controls each have an exact expected failure:

- Refuse live root layout: frozen-template extent fails the baseline check.
- Omit fast root layout: the later numeric resize fails its extent check.
- Skip baked sprite normalization: the verified original identity check fails.
- Omit a later verified alias: the original FX asset cannot resolve.
- Skip serialized sprite slots: the semantic original sprite cannot resolve.
- Plant double root pose by removing detached normalization: world-frame geometry
  fails. This planted causal control is not a claim that the previous producer
  emitted such a packet; current producers omit root-local pose samples.
- Omit all model-face original FX registration: the native effect texture fails
  validation before presentation.

The boundary UIInfoTools object reproduces the audited native serialized slot
shape with actual exported artwork; it does not execute the game's full
ItemCardUI/shop lifecycle. Receiver clones stay inert. These checks establish
specific production asset/geometry invariants, not headset correctness, native
shopping callbacks or a transport deadline. The existing scheduler/motion and
handoff suites cover those separate boundaries.

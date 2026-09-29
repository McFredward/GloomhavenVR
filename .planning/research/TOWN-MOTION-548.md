# Town occupation motion — build 548

The build-547 hardware review rejects the authored mechanical hand cycles. The previous
implementation fixed the elbow poles, overrode the arm pose completely and assigned the
entire lower body to Hips despite having thigh/shin/foot bones. A generated body clip
alone would therefore have lost most of its movement.

## Chosen motion source

[NVIDIA Kimodo-SOMA-RP-v1.1](https://huggingface.co/nvidia/Kimodo-SOMA-RP-v1.1)
generates skeletal motion with pose and end-effector constraints. Its model card lists
global commercial deployment; the [NVIDIA Open Model License](https://www.nvidia.com/en-us/agreements/enterprise-software/nvidia-open-model-license/)
permits worldwide use, does not claim ownership of outputs, and excludes outputs from
Derivative Models. The software is Apache-2.0. Model weights are not shipped.

Generation runs offline on this machine's CPU, with four threads. No paid API calls were
made. Empty-text conditioning is an explicitly supported path in the model's `_generate`
implementation; the authoring adapter accepts **only** empty text and supplies the exact
zero embedding used for it. This does not access the gated Llama text encoder. Full-body
boundary poses, planted foot constraints, root constraints and hand contact keys specify
the occupations. Kimodo supplies the intermediate body/shoulder/elbow motion. These are
**AI-generated clips, not recordings of an actor**. Runtime finger and object contact
correction remains necessary; the model does not know the actual game coin or skin mesh.

The neutral starting pose is frame zero of NVIDIA's published generated
`kimodo/assets/demo/examples/kimodo-soma-rp/04_ee_constraint/motion.npz`. Only that pose is
used as an anatomical authoring reference. Source commit, input/output hashes, seeds,
steps and elapsed generation time are retained with the receipts. No prompt, key, account
or cloud service is required when playing.

## Other evaluated sources

- [fal Hunyuan Motion](https://fal.ai/models/fal-ai/hunyuan-motion) lists $0.08/generation
  and FBX output. It was **not called**: the [upstream license §5(c)](https://raw.githubusercontent.com/Tencent-Hunyuan/HY-Motion-1.0/master/License.txt)
  also restricts outputs outside its territory (EU, UK and South Korea excluded), including
  hosted use. A generic commercial label does not establish an exception.
- [DeepMotion](https://www.deepmotion.com/pricing-animate3d) has body/hand/face capture,
  but API access is offered through sales and its free tier is noncommercial. There is
  no existing authorized account or suitable filmed performance in this checkout.
- [Kimodo installation](https://research.nvidia.com/labs/sil/projects/kimodo/docs/getting_started/installation.html)
  documents the gated Llama dependency for text conditioning. The attempted standard
  local text path returned HTTP 401; it was abandoned. Constraint-only inference avoids
  that dependency using the model's own supported empty-text behavior.

## Runtime and authoring contracts

`generate-town-motion.py` retains unmodified model output and a receipt; it never replaces
an existing output automatically. `bake-town-motion.py` converts the output into compact
30 Hz contact positions, elbow guides and body quaternion deltas. The loop seam is closed
over eight frames; playback always advances forward. The runtime interpolates these samples
from the existing shared occupation clock, including authority handovers and interruption.
No new network stream, model inference, per-frame allocation or service dependency is added.

Actual coin ownership still changes during stationary contact windows, with a rigid pinch
while held. Unequal observation intervals separate transfers. The enchantress keeps the
actual offered-palm/card anchor and eye/head tracking. The priestess uses a quiet generated
body phrase while keeping the two prayer palms in contact.

The leg-weight authoring pass changes only vertices below 0.96 m whose existing weights belong exclusively to lower-body bones; it verifies all other weights are unchanged. It preserves the facial
shape keys, hand geometry and UVs, uses at most four weights, and makes coincident seam
vertices agree. Runtime foot placement corrects the generated stance against the actual
imported limb lengths. It does not move the resident station or rewrite gameplay geometry.

## Evidence

Four 100-step clips were generated on CPU (merchant outward/return transfer, enchantress,
priestess); generation took 108.6, 76.8, 193.1 and 158.9 seconds respectively, with no API cost.
Receipts and model/source hashes are in `town548-motion/`. Baked body retarget gains preserve
motion timing while reducing chest/spine rotation to 50%, clavicles to 40%, and neck to 15%:
independent head gaze must not stack with a generated 22-degree neck turn.

The phase/packet/contact portable suite passes including its negative controls. Actual imported
rig contact validation measures maximum planted-foot displacement of 0.35 mm, 0.30 mm and
0.24 mm (merchant, priestess, enchantress). Body sample steps are at most 1.24 degrees at 90 Hz;
the final loop seam step is 0.22 degrees. Final serialized prefabs were rebuilt from the combined facial and leg-weight FBX, then
reviewed in Unity work/attention renders. Replacing only an FBX had left old eye transforms
inside an earlier review prefab; that intermediate output was rejected. The final matching
bundle passes 185,546 contact/activity assertions and sixteen negative controls. A height-only initial leg mask also affected low resting hands; the corrected
mask excludes every vertex with any existing arm/finger/torso influence.
The enchantress plays at the generated 10.2-second duration. Effect and palm-roll keys follow
that duration; her visit transition remains the shared 0.65 seconds. A down-to-up palm turn
therefore peaks at 4.62 degrees/frame at 90 Hz, with 65% of the twist assigned to the forearm.
The actual contact test allows 5 degrees for that transition and retains the 4-degree work bound.

The final diagnostic work envelope (station-space) stays within x [-0.471, 0.551], with arm
reach over the counter down to z 0.116. Priestess hands stay clasped and her work envelope
starts at z 0.336. Grounded skin extends down to approximately y 0.000, without raised feet.
All non-leg weights are asserted unchanged in the per-resident leg-weight receipts.

Actual Unity work renders use 8 fps and do not establish headset frame pacing. The offered-palm
transition uses 24 fps. Native coin material, final cabinet geometry and environment lighting
are integration responsibilities; the motion render uses the physical diagnostic coin volume.
Hardware naturalness remains a visual review, not a property established by an assertion count.

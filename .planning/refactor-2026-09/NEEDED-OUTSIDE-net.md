# NEEDED-OUTSIDE — lane **net** (refactor 2026-09)

> **[verified 2026-09-08 against `49ceab21` (ModBuild 483)] SOME OF THESE HAVE BEEN APPLIED, AND
> THIS FILE DOES NOT SAY WHICH.** A NEEDED-OUTSIDE list is written at the moment the lane closes
> and is never revisited, so its standing claim that "nothing here has been applied" decays into a
> false statement the first time the integrator lands one of them. Per-item status was **spot
> checked, not exhaustively re-derived** — check the item against source before acting on it, and
> read the line numbers as advisory (they are from the lane's base commit, not from `49ceab21`).
>
> Verified in this pass:
> - **P1 (the gaze-bias lean) is STILL OPEN.** No shared helper exists — `grep -rn "GazeBiasStep"
>   src/` returns nothing, and both `CardFan.UpdateGazeBias` and
>   `RemoteHandFan.UpdateGazeBias` are still there, term for term. The six constants are still
>   pinned by `check-mirrors.sh` (green), which remains the weaker half of what a merge would give.
> - P2 and P3 were **not** re-checked.

Three changes lane net needs in another lane's files, and nothing else. All three are the same
class — **parallel construction across the Net↔Cards seam**: one concept the player experiences
once, implemented twice, with the two copies in two lanes' file sets. Per BRIEF §1.4 a merge whose
halves straddle lanes is a finding, not a commit, so none of these was made.

**They are NOT mirror pairs in the protected sense.** `check-mirrors.sh` protects a local↔remote
pair whose two bodies must be allowed to differ (the remote reads the OWNER's value, the local
reads the player's own). In all three cases below the two bodies are the same computation over
different inputs, and a difference between them could only ever be a bug — which is why they are
worth merging rather than pinning. In P1 the constants are ALREADY pinned by `check-mirrors.sh`
(six entries), which is the weaker half of what a merge would give.

Ranked by risk reduced. P1 is the only one with a hardware-visible failure mode.

---

## P1 — the gaze-bias lean: 39 duplicated lines, six pinned constants, one latch

**Files.** `src/GloomhavenVR/Cards/CardFan.cs:1297-1370` (lane **cards**) ↔
`src/GloomhavenVR/Net/Remote/RemoteHandFan.cs:5352-5423` (lane **net**, mine).

**What is duplicated.** The whole hysteresis lean: the horizontal projection, `SignedAngle`, the
committed-side LATCH (centre → a side past `GazeBiasDeadzoneDeg`, a side → centre inside
`GazeBiasReleaseDeg`, an opposite side only on a firm past-deadzone crossing), the smoothstep
weight, the clamp, the exponential ease and the 0.05° floor. Byte-identical after normalisation
except for two things, both of them envelope rather than logic:

| | `CardFan` (local) | `RemoteHandFan` (mirror) |
|---|---|---|
| time source | reads `Time.unscaledDeltaTime` itself | takes `dt` from its caller and clamps `Max(dt, 0)` |
| log line | `Cards` scope, `Fan gaze-bias:` | `Net` scope, `Remote hand fan [player N] gaze-bias:` |

Both clamp the step to `0.05f`. **The mirror reads no viewer value** — it is driven by the peer's
own synced head — so the 1:1 rule is satisfied by either shape.

**Why it matters more than the line count.** The six constants are pinned by `check-mirrors.sh`
(`hand fan gaze-bias deadzone / release / full angle / max yaw / gain / smoothing`), so a changed
NUMBER fails the build — but a changed RULE does not. Editing the latch on one side only (adding a
third band, changing the release comparison to `<=`, reordering the two `else if` arms) leaves
every gate green and makes a peer's fan lean differently from its owner's, which is a 1:1 breach in
ANIMATION that no checker in this tree can see. That is precisely the half R2 found nobody was
checking.

**Proposed change (lane cards owns the new file).**

```diff
--- /dev/null
+++ b/src/GloomhavenVR/Cards/GazeBiasLean.cs
@@
+namespace GloomhavenVR.Cards;
+
+/// <summary>
+/// THE FAN'S GAZE LEAN, ONCE — the hysteresis that turns a fan toward the head that is reading it.
+/// Used by the local fan (<see cref="CardFan"/>) and by every peer's mirrored fan
+/// (<c>Net.RemoteHandFan</c>), which must lean the same WAY by the same number of DEGREES or the
+/// 1:1 rule is broken in ANIMATION — a breach no checker in this tree can see, because
+/// check-mirrors.sh pins the six CONSTANTS and nothing pins the RULE.
+///
+/// <para>The committed side is a LATCH, not the live sign: centre commits only past the wide
+/// deadzone, a committed side releases only inside the smaller band, and an opposite side is taken
+/// only on a firm past-deadzone crossing. Written for the dither the raw sign produced — a head
+/// shaking across the fan centre made SignedAngle flip every few degrees and the fan swung
+/// indecisively.</para>
+/// </summary>
+internal struct GazeBiasLean
+{
+    internal const float DeadzoneDeg = 20f;
+    internal const float ReleaseDeg = 10f;
+    internal const float FullDeg = 42f;
+    internal const float Gain = 0.6f;
+    internal const float MaxYawDeg = 32f;
+    internal const float Smoothing = 9f;
+
+    private int _side;
+    private float _yaw;
+
+    internal int Side => _side;
+    internal float Yaw => _yaw;
+
+    /// <summary>Advance the lean and return the yaw in degrees about world up (0 = centred).
+    /// <paramref name="dt"/> is UNSCALED seconds, clamped to 0.05 so one long frame cannot snap
+    /// the lean; <paramref name="gazeOffset"/> is returned for the caller's own log line.</summary>
+    internal float Update(UnityEngine.Vector3 away, UnityEngine.Vector3 headForward, float dt,
+                          out float gazeOffset)
+    {
+        // …the body of CardFan.UpdateGazeBias, verbatim, with `Time.unscaledDeltaTime` replaced by
+        // the clamped `dt` parameter and the VRLog call removed (each caller keeps its own line).
+    }
+}
```

```diff
--- a/src/GloomhavenVR/Cards/CardFan.cs
+++ b/src/GloomhavenVR/Cards/CardFan.cs
@@ -1256,1279 +...
-    private const float GazeBiasDeadzoneDeg = 20f;      // …and the five siblings
-    private int _gazeSide;
-    private float _gazeBiasYaw;
+    private GazeBiasLean _gaze;
@@ -1297,1370 +...
-    private float UpdateGazeBias(Vector3 away, Vector3 headForward)
-    {
-        …73 lines…
-    }
+    private float UpdateGazeBias(Vector3 away, Vector3 headForward)
+    {
+        float yaw = _gaze.Update(away, headForward, Time.unscaledDeltaTime, out float gazeOffset);
+        // the existing throttled diagnostic, unchanged, reading _gaze.Side and gazeOffset
+        return yaw;
+    }
```

**My side, once that lands** (lane net, my commit — NOT done, because it cannot compile before the
above): `RemoteHandFan` drops its six constants and its copy and calls
`_gaze.Update(away, headForward, dt, out float gazeOffset)`, keeping its own `Remote hand fan …
gaze-bias` line verbatim.

**Guard expectation.** `CHANGED` confined to `CardFan`, `RemoteHandFan` and the new type; six
`check-mirrors.sh` entries retire (the file fails on an entry whose two sides no longer exist,
which is how it tells you). Wire: untouched — nothing here rides a packet.

---

## P2 — the enhancement-sticker write: 36 duplicated lines, the game's own two arms

**Files.** `src/GloomhavenVR/Cards/HandFanEnhancementRefresh.cs:155-203` (lane **cards**) ↔
`src/GloomhavenVR/Net/Remote/RemoteFanEnhancementRefresh.cs:170-218` (lane **net**, mine).

**What is duplicated.** The per-sticker body: read `sticker.Enhancement?.Enhancement` behind a
try/catch that counts a failure and continues, compare against `sticker.EnhancementType`, skip when
equal, capture the first before/after pair for the log, then write through **the game's own two
arms** — `EnhancedAreaHex.RemoveEnhancement()/ApplyEnhancement()` and
`EnhancementButton.UpdateEnhancement(want)` — with a `continue` for any third widget kind the game
itself does not draw. Identical except the counter names (`writtenOnThisCard` vs
`writtenOnThisSlab`) and the log prefix.

**Why it matters.** The two arms are a transcription of `SaveDataShared.ApplyEnhancementIcons`, and
the comment above both copies says so. A future game build that adds a third sticker kind, or moves
`UpdateEnhancement`, has to be found TWICE — and the copy that gets missed is the mirrored one,
which is the one nobody plays against directly.

**Proposed change (lane cards owns the new file).** Extract the loop body — not the loop — so both
callers keep their own population walk, their own counters and their own log:

```diff
--- /dev/null
+++ b/src/GloomhavenVR/Cards/EnhancementStickerWriter.cs
@@
+namespace GloomhavenVR.Cards;
+
+/// <summary>
+/// WRITING ONE ENHANCEMENT STICKER, the game's own way — <c>SaveDataShared.ApplyEnhancementIcons</c>'
+/// two arms, transcribed once. Used by the local hand fan and by the mirrored one; a third widget
+/// kind, or a moved method, is then found in ONE place instead of one place and a half.
+/// </summary>
+internal static class EnhancementStickerWriter
+{
+    internal enum Result { Unchanged, Written, Unreadable, UnknownKind }
+
+    /// <summary>Bring one sticker to what a fresh print would draw. <paramref name="had"/> and
+    /// <paramref name="want"/> are reported for the caller's before/after log line.</summary>
+    internal static Result Apply(EnhancementButtonBase sticker, out EEnhancement had,
+                                 out EEnhancement want, out string failure)
+    {
+        // …the body of the two copies, verbatim…
+    }
+}
```

```diff
--- a/src/GloomhavenVR/Cards/HandFanEnhancementRefresh.cs
+++ b/src/GloomhavenVR/Cards/HandFanEnhancementRefresh.cs
@@ -155,203 +...
-                try { … } catch { failures++; VRLog.Debug(…); continue; }
-                …44 lines…
+                switch (EnhancementStickerWriter.Apply(sticker, out EEnhancement had,
+                                                       out EEnhancement want, out string failure))
+                {
+                    case EnhancementStickerWriter.Result.Unreadable:
+                        failures++;
+                        VRLog.Debug(Scope, $"Enhancement refresh: card {abilityCardId} slot "
+                                           + $"{sticker.EnhancementSlot} kept its sticker — the game "
+                                           + $"model was unreadable: {failure}");
+                        continue;
+                    …
+                }
```

**Guard expectation.** `CHANGED` confined to the two refreshers and the new type. No wire, no
config, no token — both log lines are kept verbatim at their call sites.

---

## P3 — the ember column: a hardware-tuned particle recipe living in two files, pinned by nothing

**Files.** `src/GloomhavenVR/Cards/Piles/PileViewer.cs:1288-1340` (lane **cards**) ↔
`src/GloomhavenVR/Net/Remote/RemoteControlBoard.cs:3748-3792` (lane **net**, mine).

**What is duplicated.** The whole recipe, value for value: box shape at `0.85` of the face with
`randomDirectionAmount = 0`, velocity `x ±0.008 / y 0.022..0.048 / z −0.018..−0.006` in local
space, low-quality noise `strength 0.006, frequency 0.35, scrollSpeed 0.12, damping`, the
four-key alpha gradient `(0,0) (0.25,1) (0.6,0.75) (1,0)`, the size curve
`(0,0.55) (0.3,1) (1,0.25)`, and a billboard/view-aligned renderer with shadows off on
`SoftCueArt.MoteTexture()`. The only difference is where the face size comes from (`w`/`h` locally,
`SlabW`/`SlabH` on the mirror) and that the local copy carries the tuning comments.

**Why it matters.** Every one of those numbers is a hardware-round residue — the velocity block
carries its own note that it was "roughly doubled since the presence pass" because a mote that
drifts two centimetres never reads as MOVEMENT, and that the −z lean is "the one depth cue
passthrough structurally cannot mask". Not one of them is in `check-mirrors.sh`. A retune of the
owner's stack silently leaves a peer's stack on the old recipe, and the difference is a slow
particle drift nobody would attribute to a copy-paste.

**Proposed change (lane worldui-front owns `SoftCueArt`).** `SoftCueArt` is already the shared home
for this family's art (`MoteTexture()` is called by both copies), so the recipe belongs beside it:

```diff
--- a/src/GloomhavenVR/WorldUI/SoftCueArt.cs
+++ b/src/GloomhavenVR/WorldUI/SoftCueArt.cs
@@
+    /// <summary>
+    /// THE EMBER COLUMN, ONCE — the drifting motes over a pile that is offering something. Every
+    /// number here is a hardware-round residue (the velocity block was doubled deliberately: a mote
+    /// that drifts two centimetres never reads as MOVEMENT, and the −z lean toward the viewer is
+    /// the one depth cue passthrough cannot mask), and the peer's copy of a stack must beat exactly
+    /// as the owner's does. It lived in two files until 2026-09 with nothing pinning either.
+    /// </summary>
+    internal static void ConfigureEmberColumn(ParticleSystem ps, float faceW, float faceH,
+                                              Shader shader)
+    {
+        // …the shape/velocity/noise/colour/size/renderer block, verbatim…
+    }
```

with `PileViewer` passing `w, h` and `RemoteControlBoard` passing `SlabW, SlabH` (the owner's slab
size — still the owner's number, so 1:1 is unaffected).

**Guard expectation.** `CHANGED` confined to `PileViewer`, `RemoteControlBoard` and `SoftCueArt`.
If the integrator would rather not move it, the fallback that costs nothing is **six new
`check-mirrors.sh` constant groups** for the velocity, noise, gradient and size keys — weaker (it
pins numbers, not the recipe) but better than the nothing there is today.

---

## Not requested, recorded

Nothing else. In particular this lane did NOT ask for:

- a `Cards.PileKind` / `Cards.ControlBoard` change of any kind — those enums' member ORDER is a
  wire constant and the two compile-time guards that defend them
  (`NetAvatarDriver.PileKindWireOrderGuard`, `LocalRigSampler.ControlBoardWireOrderGuard`) must
  stay exactly where they are, in files that see both `Cards.` and `NetProtocol.`;
- any `Core/Loc` or `Defaults/` entry: the mixed language on a peer's board is the user's own
  ruling, and no tuning value was touched;
- `record id 46`, which is the integrator's, and no `ModBuild` bump.

# ModBuild 351, Modal/Surfaces/Interact lane — work that belongs to other lanes

> ### ✅ CONSUMED, AND ITS CAUSE WAS WRONG — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> Item 12's **fix** was applied almost verbatim by the pick-placard lane; item 12's **cause**, as
> diagnosed here, was not the real one. Read
> [`LANE-PICKBANNER-356-NEEDED-OUTSIDE.md`](LANE-PICKBANNER-356-NEEDED-OUTSIDE.md) for the
> correction — the placard was cut by a 96-byte wire cap against a 107-byte composed German line,
> not by the layout reason argued below. Nothing here is outstanding.

This lane owns `WorldUI/Modal/**`, `WorldUI/Surfaces/**` and `Hands/Interact/**`. Everything below
was diagnosed here and must be applied by the lane that owns the file. Nothing in this document has
been applied.

---

## ITEM 12 — the instruction placard is cut off on the right and at the bottom

**User, verbatim:** *"Beim Test hatte die Anweisung so einen abgeschnitten Text oben (siehe
abgeschnittener-text.jpg)."*

### The object

It is the **pick-status placard** (`PickStatusBanner`), the parchment strip that hovers above the
board's top edge. The string is `Loc` key `pick_confirm_hint`
(`src/GloomhavenVR/Core/Loc/Loc.cs:365`), composed with the character name at
`src/GloomhavenVR/Cards/Driver/CardsDriver.6.Flows.cs:459`:

> `Testr: Alle Karten liegen — mit der Board-Taste abschließen (oder eine Karte zum Tauschen zurücknehmen)`

### The sizing rule today, and why it is the wrong rule

Two independent hard-coded sizes and **nothing measures the text**:

| thing | size | where |
|---|---|---|
| parchment plate (a unit Quad, localScale) | `0.44 × 0.055` m | `Cards/Tray/PlayTray.5.Status.cs:675` |
| TMP label rect (`sizeDelta`) | `0.42 × 0.048` m | `Cards/Tray/PlayTray.5.Status.cs:690` via `Core/TmpFit.cs:70` |

`TmpFit.Fit` writes a **fixed** `sizeDelta`, turns auto-sizing on, and sets
`overflowMode = TextOverflowModes.Overflow` (`Core/TmpFit.cs:70-80`). Overflow is the right policy —
it exists because ModBuild 281 shipped `"AUSWAHL BEEN"` and the ruling was *"Der Text muss immer
voll lesbar sein"* — but it removes the vertical constraint from auto-sizing, so the shrink loop
only has to make each **line** fit the width. A string that needs more lines than the 0.048 m box
holds is drawn anyway, off the bottom of the 0.055 m parchment and onto the dark board, where it
reads as "cut off". That is exactly the screenshot: two legible lines, the second one's descenders
against the plate edge, and a third line that is drawn but invisible.

**The panel is never sized from its text.** That is the rule to fix, not the string.

### The fix

**The width stays** — 0.42 / 0.44 m is a board-relative dimension the user tuned, and wrapping is
stable only if the width is stable. **The height follows the text**, and it follows what TMP
actually *drew*, not a model of it (`GetRenderedValues` after `ForceMeshUpdate`, the same readback
`TmpFit.VerifyAndReport` already uses at `Core/TmpFit.cs:186-194`). It is **grow-only**, so a short
line keeps the authored look exactly.

Only the **plate** is resized, never the label rect: growing the label's own rect could feed back
into auto-sizing, and with `Overflow` the label does not need a truthful height — TMP centres the
block on the rect, so a plate grown symmetrically about the same centre covers it.

Three files, and the third is a mirror that must not drift.

---

#### Patch A — `src/GloomhavenVR/Core/TmpFit.cs`

Add, immediately after `Fit`:

```csharp
    /// <summary>
    /// SIZE A BACKING PLATE FROM WHAT THE LABEL ACTUALLY DREW (ModBuild 351).
    ///
    /// <para><b>USER REPORT, verbatim, item 12:</b> <i>"Beim Test hatte die Anweisung so einen
    /// abgeschnitten Text oben (siehe abgeschnittener-text.jpg)."</i> The pick placard's plate and
    /// its text box are two hard-coded sizes and neither is derived from the string. <see
    /// cref="Fit"/> deliberately never truncates (see the class header), and its
    /// <c>TextOverflowModes.Overflow</c> also takes the HEIGHT out of the auto-size loop — so a
    /// string needing more lines than the box holds is drawn past the bottom of its own parchment,
    /// onto whatever is behind it. That is not a clipping bug, it is a plate that was never told
    /// how big the text is.</para>
    ///
    /// <para><b>MEASURED, NOT MODELLED.</b> The extent comes from <c>GetRenderedValues</c> after a
    /// forced mesh update — the same readback <see cref="FitCapLabel"/>'s verifier uses — so it is
    /// the real font, the real auto-sized size and the real line breaks. A modelled height would be
    /// a second opinion about the same thing, and this project has a ledger of those.</para>
    ///
    /// <para><b>GROW-ONLY, so a short caption is untouched.</b> The authored size stays the
    /// MINIMUM: the common case (one short line) renders byte-identically to before, and only a
    /// string that genuinely does not fit moves anything.</para>
    ///
    /// <para><b>FAILS TO THE AUTHORED SIZE.</b> A label with no font asset yet returns zeros from
    /// the readback; a dead probe must never produce a layout, so a non-positive or NaN measurement
    /// returns the authored size unchanged rather than collapsing the plate.</para>
    /// </summary>
    /// <param name="tmp">The label whose drawn extent decides the plate.</param>
    /// <param name="authoredPlate">The tuned plate size, local metres — kept as the minimum.</param>
    /// <param name="paddingMeters">Margin per side between the drawn text and the plate edge.</param>
    internal static Vector2 PlateSizeFor(TMP_Text? tmp, Vector2 authoredPlate, float paddingMeters)
    {
        if (tmp == null)
            return authoredPlate;
        Vector2 rendered;
        try
        {
            tmp.ForceMeshUpdate();
            rendered = tmp.GetRenderedValues(false);
        }
        catch
        {
            return authoredPlate;
        }
        if (!(rendered.x > 0f) || !(rendered.y > 0f)
            || float.IsNaN(rendered.x) || float.IsNaN(rendered.y))
            return authoredPlate;
        return new Vector2(
            Mathf.Max(authoredPlate.x, rendered.x + 2f * paddingMeters),
            Mathf.Max(authoredPlate.y, rendered.y + 2f * paddingMeters));
    }
```

---

#### Patch B — `src/GloomhavenVR/Cards/Tray/PlayTray.1.Core.cs`

At `:168`, beside `_pickBannerLabel`:

```csharp
    private Transform? _pickBannerPlate;   // ModBuild 351: grown to contain the drawn text
```

and at `:1478`, beside `_pickBannerLabel = null;`:

```csharp
        _pickBannerPlate = null;
```

---

#### Patch C — `src/GloomhavenVR/Cards/Tray/PlayTray.5.Status.cs`

1. In `EnsurePickBanner`, replace

```csharp
        plate.transform.localScale = new Vector3(0.44f, 0.055f, 1f);
```

with

```csharp
        plate.transform.localScale = new Vector3(PickPlateSize.x, PickPlateSize.y, 1f);
        _pickBannerPlate = plate.transform;
```

2. Replace the two literals in the same method's `TmpFit.Fit` call

```csharp
        Core.TmpFit.Fit(_pickBannerLabel, 0.42f, 0.048f, maxFontSize: 0.30f, wrap: true);
```

with

```csharp
        Core.TmpFit.Fit(_pickBannerLabel, PickTextBox.x, PickTextBox.y,
                        maxFontSize: PickMaxFont, wrap: true);
```

3. Add, next to `PickBannerBase`:

```csharp
    /// <summary>The placard's AUTHORED plate size, local metres — now a MINIMUM rather than the
    /// size: <see cref="SizePickBannerPlate"/> grows it to contain a line that needs more room.
    /// Internal so <c>Net.RemotePickBanner</c> reads the same numbers instead of repeating them,
    /// which is how the owner's placard and a peer's mirror stopped agreeing before.</summary>
    internal static readonly Vector2 PickPlateSize = new(0.44f, 0.055f);

    /// <summary>The text box inside <see cref="PickPlateSize"/> — the width is the tuned,
    /// board-relative dimension and it is FIXED, because wrapping is only stable if the width
    /// is.</summary>
    internal static readonly Vector2 PickTextBox = new(0.42f, 0.048f);

    /// <summary>Preferred font size for a short line (auto-size ceiling).</summary>
    internal const float PickMaxFont = 0.30f;

    /// <summary>Margin per side between the drawn text and the parchment edge, local metres —
    /// the authored plate's own horizontal margin ((0.44−0.42)/2 = 0.010 m), used on both axes so
    /// a grown plate keeps the look the short line has.</summary>
    internal const float PickPlatePadding = 0.010f;
```

4. In `SetPickStatus`, after the text write:

```csharp
        EnsurePickBanner();
        if (_pickBannerLabel != null)
            _pickBannerLabel.text = banner;
        SizePickBannerPlate();                       // <-- ADD
        if (_pickBannerRoot != null && !_pickBannerRoot.activeSelf)
```

5. Add the method:

```csharp
    /// <summary>
    /// THE PLACARD IS SIZED FROM ITS TEXT (ModBuild 351, user item 12 — "so einen abgeschnitten
    /// Text"). Grow-only, measured off the drawn glyphs, called from the one change-gated place
    /// the text is written so it costs nothing per frame. See <c>Core.TmpFit.PlateSizeFor</c> for
    /// why the measurement is a readback and not a model, and why only the PLATE moves.
    /// </summary>
    private void SizePickBannerPlate()
    {
        if (_pickBannerPlate == null || _pickBannerLabel == null)
            return;
        Vector2 want = Core.TmpFit.PlateSizeFor(_pickBannerLabel, PickPlateSize, PickPlatePadding);
        Vector3 have = _pickBannerPlate.localScale;
        if (Mathf.Abs(have.x - want.x) < 1e-4f && Mathf.Abs(have.y - want.y) < 1e-4f)
            return;
        _pickBannerPlate.localScale = new Vector3(want.x, want.y, 1f);
        VRLog.Info("Cards", $"Pick banner plate sized from the drawn text: {want.x:F3} x "
                            + $"{want.y:F3} m (authored minimum {PickPlateSize.x:F3} x "
                            + $"{PickPlateSize.y:F3} m).");
    }
```

---

#### Patch D — `src/GloomhavenVR/Net/Remote/RemotePickBanner.cs` (the mirror — do not skip)

Its own comments promise a peer's placard is identical to the owner's, and it repeats the three
numbers by hand at `:49`, `:52` and `:53`. Replace them with reads of the owner's constants and
apply the same grow:

```csharp
    private static readonly Vector2 PlateSize = Cards.PlayTray.PickPlateSize;
    private static readonly Vector2 TextBox  = Cards.PlayTray.PickTextBox;
    private const float MaxFont = Cards.PlayTray.PickMaxFont;
```

Keep the `MeshRenderer` returned by `BoardVisual.Quad` in a field:

```csharp
    private readonly Transform _plate;
    ...
    _plate = plate.transform;
```

and in `Apply`, after `RemoteBoardContent.SetText(_label, _shown);`:

```csharp
        // ModBuild 351: the peer's plate follows the peer's text exactly as the owner's does —
        // the same helper, the same authored minimum, the same padding. A mirror that keeps the
        // old fixed plate would show the owner's long line spilling off a peer's parchment.
        Vector2 want = Core.TmpFit.PlateSizeFor(_label, PlateSize, Cards.PlayTray.PickPlatePadding);
        if (_plate != null
            && (Mathf.Abs(_plate.localScale.x - want.x) > 1e-4f
                || Mathf.Abs(_plate.localScale.y - want.y) > 1e-4f))
            _plate.localScale = new Vector3(want.x, want.y, 1f);
```

### How to verify it without a headset

The fit is a **readback of the drawn mesh**, so it cannot be checked by arithmetic — that is the
point of it, and it is why no number in this patch predicts a height. What CAN be checked without a
headset:

* `scripts/check-mirrors.sh` — patch D removes three hand-copied literals, so the owner and the
  mirror are the same constants by construction rather than by agreement.
* The `Pick banner plate sized from the drawn text: W x H m` line names the measured size; run the
  scenario once and read it. For his string it must report a height **above** the authored 0.055 m
  (the screenshot shows three drawn lines in a two-line box); a report of exactly 0.055 m would mean
  the readback returned zeros and the fallback fired, which is a different bug and says so.

---

## Nothing else is needed outside this lane.

Items 1 and 2 and the ModBuild 351 spawn-height ruling are all inside `WorldUI/Modal/**` and
`WorldUI/Surfaces/**` and are applied in this lane's commit.

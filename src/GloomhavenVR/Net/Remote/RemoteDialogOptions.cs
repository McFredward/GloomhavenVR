using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// THE OPTION ROW OF A PEER'S <c>DialogPopup</c>, BUILT OUT OF THE RECEIVER'S OWN BUTTON PREFAB —
/// the source row <see cref="RemoteDecisionWidgets"/> mirrors for wire prompt kind
/// <c>DecisionKindDialogPopup</c>.
///
/// ─── WHY THIS PROMPT NEEDED A BUILDER AND THE OTHER TWO DID NOT ────────────────────────────────
/// The take-damage panel and the short-rest dialog are both LIVE objects every client owns, so the
/// mirror clones what is already there. A <c>DialogPopup</c>'s options are neither: they are POOLED
/// (<c>HelperTools.NormalizePool(ref optionButtons, optionButtonPrefab, holder, options.Length)</c>,
/// DialogPopup.cs:176), so the receiver holds as many buttons as ITS OWN last dialog needed —
/// possibly none, possibly fewer than the owner is showing. Growing that pool would mean calling
/// the game's own normalize on the receiver's live popup, i.e. WRITING GAME STATE FROM PRESENTATION
/// CODE, which is a standing prohibition here.
///
/// So this class does the one thing that is allowed and is also honest: it reads the popup's
/// serialized <c>optionButtonPrefab</c> — a reference, not a mutation — and instantiates its OWN
/// copies, as many as the owner has options.
///
/// ─── AND IT IS STILL A SOURCE, NOT A DISPLAY ───────────────────────────────────────────────────
/// What it builds is fed to <see cref="RemoteWidgetMirror"/> exactly like the other two prompts'
/// live rows, which is what keeps this class small and keeps ONE painter for all three: the mirror
/// clones it into the board's own world-space host, strips every non-presentation component, fits
/// it into the same mount envelope, and <see cref="RemoteDecisionWidgets"/> paints the states onto
/// the clone from the wire. Nothing here is ever shown to anybody.
///
/// ─── NO AWAKE EVER RUNS ────────────────────────────────────────────────────────────────────────
/// The row is built under a permanently INACTIVE holder, which is the same trick the mirror uses
/// for its own clone host: a prefab instantiated into an inactive parent is inactive in hierarchy,
/// so <c>InputButton</c>, <c>ExtendedButton</c> and <c>InteractabilityIsolatedUIControl</c> never
/// reach <c>Awake</c> and never register with a singleton, a hotkey controller or the
/// interactability manager. The holder is never activated. The CLONE the mirror makes of it is
/// stripped of all three anyway, so there are two independent reasons nothing here can act.
///
/// ─── ZERO NEW WIRE BYTES ───────────────────────────────────────────────────────────────────────
/// Everything this needs was already riding: the prompt KIND (record 24), the option WORDINGS
/// (record 12 — the owner's rendered labels, which is the one thing a receiver genuinely cannot
/// produce, because a <c>DialogOption.text</c> is a runtime string and not a localization key) and
/// the per-option STATES including the owner's hover and press (record 24 bits 0..4). No role code
/// was added: the options are index-aligned with those records by construction, and "the i-th
/// pooled button" is all the identity there is to have.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY plumbing over a wire-fed display — the buttons are LOCAL pixels
/// built from this client's own prefab; only the wordings and states ride records 12 and 24. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteDialogOptions
{
    /// <summary>Cap on how many option buttons will ever be built — the same bound the record
    /// carries, so a hostile or garbled count can never make this instantiate a crowd.</summary>
    private const int MaxOptions = NetProtocol.DecisionStateMaxOptions;

    /// <summary>Fallback horizontal gap between two option buttons, as a FRACTION of a button's
    /// height, used only when the game's own layout group cannot be read. The real number is taken
    /// from that group (see <see cref="ResolveSpacing"/>); this exists so a prefab reshuffle costs a
    /// slightly different gap rather than a row with its buttons touching.</summary>
    private const float FallbackGapPerHeight = 0.25f;

    private readonly RectTransform _holder;
    private RectTransform? _row;
    private readonly List<Selectable> _buttons = new(MaxOptions);

    /// <summary>The wordings the current row was built for — the rebuild gate. A row is rebuilt when
    /// the owner's options CHANGE, never per tick: instantiating prefabs is the expensive thing this
    /// class does and a docked prompt's options stand still until the prompt does.</summary>
    private string[] _builtFor = System.Array.Empty<string>();

    /// <summary>The option buttons of the row, in wire option order. Empty until a row is built.</summary>
    internal IReadOnlyList<Selectable> Buttons => _buttons;

    /// <summary>Why the last <see cref="Resolve"/> produced nothing, for the caller's diagnostic
    /// line. Empty after a successful build.</summary>
    internal string Reason { get; private set; } = "not built";

    /// <summary>
    /// Create the inactive holder this class builds into. Parented under
    /// <paramref name="parent"/> only so it dies with the board — nothing under it is ever drawn,
    /// and the holder is deactivated before anything is put inside it.
    /// </summary>
    internal RemoteDialogOptions(Transform parent)
    {
        // A RECT transform, not a plain one: RemoteWidgetMirror sizes its pivot from the SOURCE'S
        // PARENT rect (AdoptParentRect), and a plain Transform there would hand it a zero size.
        // The holder is sized to the row it holds, which is what a real UI parent would be.
        var holder = new GameObject("DialogOptionSource", typeof(RectTransform))
            .GetComponent<RectTransform>();
        holder.SetParent(parent, worldPositionStays: false);
        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one;
        // BEFORE anything is instantiated into it — an active frame here would be an Awake.
        holder.gameObject.SetActive(false);
        _holder = holder;
    }

    internal void Destroy()
    {
        if (_holder != null)
            Object.Destroy(_holder.gameObject);
    }

    /// <summary>
    /// The source row for <paramref name="lines"/> — the owner's option wordings, in wire order.
    /// Returns null (with <see cref="Reason"/> set) when this client cannot build one, which the
    /// caller renders as the mod-drawn plate row exactly as before.
    ///
    /// <para>Change-gated on the wordings: the same options resolve to the SAME row object, so the
    /// mirror sees an unchanged source and does not rebuild its clone.</para>
    /// </summary>
    internal RectTransform? Resolve(string[]? lines)
    {
        try
        {
            if (lines == null || lines.Length == 0)
                return Down("the owner published no option wordings (record 12 is empty)");
            int n = lines.Length < MaxOptions ? lines.Length : MaxOptions;

            if (_row != null && SameLines(lines, n))
            {
                Reason = string.Empty;
                return _row;
            }

            GameObject? prefab = OptionButtonPrefab();
            if (prefab == null)
                return Down("this client's own DialogPopup has no optionButtonPrefab to copy " +
                            "(UIManager absent, or the prefab field is null)");

            Build(prefab, lines, n);
            if (_buttons.Count == 0)
                return Down("the option prefab carried no ExtendedButton — nothing to build a row " +
                            "from (a prefab reshuffle)");
            Reason = string.Empty;
            return _row;
        }
        catch (System.Exception e)
        {
            return Down($"building the option row failed ({e.Message})");
        }
    }

    private RectTransform? Down(string reason)
    {
        if (Reason != reason)
        {
            Reason = reason;
            VRLog.Info("Net", $"Remote dialog options: no game-button row — {reason}. The " +
                              "mod-drawn plate row stands in, which is what every build before " +
                              "ModBuild 303 drew for this prompt.");
        }
        return null;
    }

    private bool SameLines(string[] lines, int n)
    {
        if (_builtFor.Length != n)
            return false;
        for (int i = 0; i < n; i++)
        {
            if (!string.Equals(_builtFor[i], lines[i], System.StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>THIS CLIENT'S OWN option-button prefab, off its own <c>DialogPopup</c>. A serialized
    /// reference READ, never a call into the popup — the distinction the class doc is about.</summary>
    private static GameObject? OptionButtonPrefab()
    {
        UIManager? manager = UIManager.Instance;
        DialogPopup? popup = manager != null ? manager.dialogPopup : null;
        return popup != null ? popup.optionButtonPrefab : null;
    }

    /// <summary>
    /// The gap the GAME puts between two option buttons — its own
    /// <c>HorizontalLayoutGroup.spacing</c> on the popup's <c>horizontalOptionsHolder</c>, in the
    /// canvas units this row is built in.
    ///
    /// <para>Read rather than chosen, for the reason every number on a mirrored board is read: a gap
    /// invented here would be a second value that has to agree with the owner's, and this project
    /// has a lint whose whole job is hunting those. The fallback is a fraction of the button height
    /// and applies only if the group is gone.</para>
    /// </summary>
    private static float ResolveSpacing(float buttonHeight)
    {
        try
        {
            UIManager? manager = UIManager.Instance;
            DialogPopup? popup = manager != null ? manager.dialogPopup : null;
            Transform? holder = popup != null ? popup.horizontalOptionsHolder : null;
            var group = holder != null ? holder.GetComponent<HorizontalLayoutGroup>() : null;
            if (group != null)
                return group.spacing;
        }
        catch (System.Exception)
        {
            // Fall through to the authored fraction — a missing layout group is a cosmetic loss.
        }
        return buttonHeight * FallbackGapPerHeight;
    }

    /// <summary>
    /// Tear down the previous row and build one of <paramref name="n"/> buttons lettered with
    /// <paramref name="lines"/>.
    ///
    /// <para>WIDTHS ARE FITTED TO THE WORDING, which is what the game does to these buttons
    /// (<c>ContentSizeFitter</c> inside the layout group) and what the mod-drawn plate row already
    /// reproduces for the same reason: an equal split is visibly wrong beside a row whose "Ja" and
    /// "Abbrechen und eine andere Karte wählen" are the same size. The INSET is measured off the
    /// prefab's own label rect, so the padding around a wording is the prefab's padding, not a
    /// number chosen here.</para>
    ///
    /// <para>The layout is done HERE rather than by a <c>HorizontalLayoutGroup</c> because the row
    /// is never active: a layout group does not run on an inactive object, and activating this one
    /// is exactly what must not happen (see the class doc). The arithmetic is a row of rectangles;
    /// the group's own spacing is read from the game.</para>
    ///
    /// <para>THAT PARAGRAPH WAS TRUE OF THE BUTTONS AND FALSE OF THEIR CAPTIONS. The label rect is
    /// layout-driven too and nobody wrote it, so every wording wrapped one glyph per line on a
    /// peer's board (user report 2026-09-05 item 16). See <see cref="LayoutCaption"/>, which writes
    /// it out of the very numbers measured here.</para>
    /// </summary>
    private void Build(GameObject prefab, string[] lines, int n)
    {
        Clear();

        var row = new GameObject("DialogOptionRow", typeof(RectTransform))
            .GetComponent<RectTransform>();
        row.SetParent(_holder, worldPositionStays: false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 0.5f);
        row.localPosition = Vector3.zero;
        row.localRotation = Quaternion.identity;
        row.localScale = Vector3.one;

        var widths = new float[n];
        var labels = new TMP_Text?[n];
        var rects = new RectTransform[n];
        var preferred = new float[n];
        var insets = new float[n];
        float height = 0f;
        float gap = 0f;
        float total = 0f;

        for (int i = 0; i < n; i++)
        {
            GameObject instance = Object.Instantiate(prefab, row);
            instance.name = $"Option{i}";
            var rect = instance.transform as RectTransform;
            if (rect == null)
            {
                Object.DestroyImmediate(instance);
                continue;
            }
            Selectable? button = ResolveButton(instance);
            if (button == null)
            {
                Object.DestroyImmediate(instance);
                continue;
            }
            TMP_Text? label = ResolveLabel(button);
            if (label != null)
                label.text = lines[i];

            float authoredW = rect.rect.width;
            float authoredH = rect.rect.height;
            if (authoredH > 0f && height <= 0f)
                height = authoredH;
            widths[_buttons.Count] = FitWidth(authoredW, rect, label,
                                              out preferred[_buttons.Count],
                                              out insets[_buttons.Count]);
            labels[_buttons.Count] = label;
            rects[_buttons.Count] = rect;
            _buttons.Add(button);
        }

        int built = _buttons.Count;
        if (built == 0)
        {
            Object.DestroyImmediate(row.gameObject);
            _row = null;
            _builtFor = System.Array.Empty<string>();
            return;
        }

        if (height <= 0f)
            height = 40f; // a prefab with no authored height: the fit scales the row anyway
        gap = ResolveSpacing(height);
        for (int i = 0; i < built; i++)
            total += widths[i];
        total += gap * (built - 1);

        // Left to right from the row's left edge, each button vertically centred — the picture a
        // horizontal layout group with this spacing produces, computed instead of run.
        float x = -total * 0.5f;
        for (int i = 0; i < built; i++)
        {
            RectTransform rect = rects[i];
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(widths[i], height);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            x += widths[i] + gap;
        }
        row.sizeDelta = new Vector2(total, height);
        _holder.sizeDelta = row.sizeDelta;

        // THE CAPTION BOX, WRITTEN RATHER THAN LAID OUT — see LayoutCaption for the defect.
        var caption = new System.Text.StringBuilder(160);
        for (int i = 0; i < built; i++)
        {
            if (caption.Length > 0)
                caption.Append("; ");
            LayoutCaption(labels[i], widths[i], height, preferred[i], insets[i], i, caption);
        }

        _row = row;
        _builtFor = new string[built];
        for (int i = 0; i < built; i++)
            _builtFor[i] = lines[i];

        // HW-VERIFY: grep DIALOG OPTION CAPTION — one line per prompt (the row is change-gated on
        // the wordings), naming per option the plate width, the caption RECT width, the font size
        // and the line count. A collapse is then a NUMBER next round instead of a screenshot.
        // FALSIFIER: `lines=1` on every option here while the headset still shows one glyph per
        // line means this write did not reach the clone — look at RemoteWidgetMirror (Neutralize /
        // Pair.Apply's rect half), not here. `capW≈0` means the write itself did not take.
        VRLog.Note("Net", $"DIALOG OPTION CAPTION: {built} button(s) built from THIS client's own " +
                          $"DialogPopup.optionButtonPrefab — [{caption}] — " +
                          $"gap {gap:F1} read from the game's own HorizontalLayoutGroup, row " +
                          $"{total:F0}x{height:F0} canvas units. plateW is the button, capW the " +
                          "caption rect this builder WRITES onto it (the prefab's own label rect is " +
                          "layout-DRIVEN and no layout runs under this permanently inactive holder, " +
                          "so it arrives collapsed); need is the wording's unconstrained preferred " +
                          "width. lines>1 with capW≥need is a wrap this builder did not ask for. " +
                          "The prefab copies live under a permanently INACTIVE holder, so no Awake " +
                          "ran on any of them; the mirror clones this row and strips it again. " +
                          "Nothing here is ever shown.");
    }

    /// <summary>
    /// SIZE THE CAPTION'S OWN RECT, because nothing else will.
    ///
    /// <para><b>THE DEFECT THIS RETIRES</b> — user report 2026-09-05, item 16, verbatim: "Bei der
    /// kurzen Rast im remote board ist was mit den buttons schief gelaufen"
    /// (<c>kaputter_text_kurze_rast.jpg</c>). The screenshot shows the two short-rest option
    /// wordings rendered as single columns of letters, ONE GLYPH PER LINE, hanging off the bottom
    /// edge of the peer's board. The plates themselves were the right shape (host log, ModBuild 447:
    /// "Remote dialog options: built 2 button(s) … row 660x42 canvas units"), so the buttons were
    /// fitted correctly and only their CAPTIONS collapsed.</para>
    ///
    /// <para><b>WHY.</b> A <c>DialogPopup</c> option button letters itself through a layout: the
    /// game's own <c>HorizontalLayoutGroup</c> + <c>ContentSizeFitter</c> drive the label's
    /// RectTransform at runtime. A DRIVEN rect is not authored, so what the prefab asset carries for
    /// it is whatever was last serialized — for this prefab, a box far narrower than any wording.
    /// This class builds its copies under a permanently INACTIVE holder (it must: an active frame
    /// here would run <c>Awake</c> on <c>InputButton</c> and register it with the game's singletons),
    /// and <b>uGUI runs no layout on an inactive object</b> — neither when the copy is made nor ever
    /// after, because <see cref="RemoteWidgetMirror"/> then strips the layout components off the
    /// clone. So the label kept the collapsed prefab rect and TMP, doing exactly what it is told,
    /// wrapped after every glyph. The BUTTON rect escaped because this builder already writes it.</para>
    ///
    /// <para>THE FIX IS THE SAME SENTENCE APPLIED ONE LEVEL DOWN: the caption rect is WRITTEN here,
    /// from numbers this class already has, instead of being waited on. Nothing is asked of the
    /// layout engine, so nothing depends on an activation that must never happen.</para>
    ///
    /// <para><b>WHAT IS WRITTEN, AND WHY IT IS 1:1.</b> The game's own option button is content-fitted
    /// around a SINGLE line (that is what the fitter does), so the mirrored one is too:
    /// word-wrapping OFF, and the box at least as wide as the wording's unconstrained preferred
    /// width. <see cref="FitWidth"/> already sized the PLATE to that same preferred width plus the
    /// prefab's own label inset, so <c>plateW − 2·inset ≥ need</c> holds by construction and the
    /// line lands inside the plate with the artist's padding on both sides. The <c>Max</c> against
    /// <paramref name="need"/> is the guard for the other direction — a pinned label whose stored
    /// offsets are not a real inset would otherwise hand back a box narrower than the text.</para>
    ///
    /// <para><b>NEVER TRUNCATE.</b> <c>overflowMode</c> is <c>Overflow</c>, the standing ruling from
    /// the ModBuild 281 "AUSWAHL BEEN" report: a caption that overhangs by a millimetre is a
    /// cosmetic complaint that can be seen and reported; a caption cut mid-word means something
    /// else. See <c>Core.TmpFit</c>, which owns the same ruling for the mod's own 3D labels — this
    /// is a uGUI rect on a game prefab and cannot use that helper, but it must not disagree with it.</para>
    ///
    /// <para>Appends this option's measured numbers to <paramref name="report"/> for the caller's
    /// one HW-VERIFY line. Wrapped whole: a measurement that throws must cost the numbers, never
    /// the row — a row that failed to build is a peer with no visible decision at all.</para>
    /// </summary>
    private static void LayoutCaption(TMP_Text? label, float plateW, float plateH,
                                      float need, float inset, int index,
                                      System.Text.StringBuilder report)
    {
        if (label == null || label.rectTransform == null)
        {
            report.Append($"#{index} plateW {plateW:F0}: NO CAPTION (the prefab's button carries no " +
                          "TMP text — nothing to size)");
            return;
        }
        float capW = Mathf.Max(need, plateW - 2f * Mathf.Max(0f, inset));
        float capH = plateH > 0f ? plateH : label.rectTransform.rect.height;
        try
        {
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(capW, capH);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;   // the game's own option button is one fitted line
            label.overflowMode = TextOverflowModes.Overflow; // never cut — see the remarks
        }
        catch (System.Exception e)
        {
            report.Append($"#{index} plateW {plateW:F0}: SIZING THREW {e.GetType().Name} " +
                          $"({e.Message}) — the caption keeps the prefab's own rect");
            return;
        }

        // MEASURED, NOT ASSUMED: the line count comes from TMP after a forced mesh update, so the
        // instrument reports what was drawn rather than what was intended. A readback that cannot
        // run (no font asset resolved yet) says so instead of printing a confident 1.
        string lines;
        try
        {
            label.ForceMeshUpdate();
            lines = label.textInfo != null ? label.textInfo.lineCount.ToString() : "n/a";
        }
        catch (System.Exception e)
        {
            lines = $"n/a ({e.GetType().Name})";
        }
        report.Append($"#{index} plateW {plateW:F0}, capW {capW:F0} (need {need:F0}, prefab inset " +
                      $"{inset:F0}), font {label.fontSize:F1}, lines={lines}, " +
                      $"\"{label.text}\"");
    }

    /// <summary>
    /// Width for one option button — the GAME's own answer when the prefab can give one, and a
    /// reconstruction of it when it cannot.
    ///
    /// <para>ASK THE GAME FIRST. <c>DialogPopup</c> puts these buttons in a
    /// <c>HorizontalLayoutGroup</c>, which sizes each child by
    /// <c>LayoutUtility.GetPreferredWidth</c> — so calling that same function on the copy is not an
    /// approximation of the owner's width, it IS the owner's width, computed by the same code on
    /// the same prefab with the same wording. It answers only when the prefab carries a layout
    /// element that provides one; a prefab that leaves the sizing to a fitter answers ≤ 0.</para>
    ///
    /// <para>THE FALLBACK IS THE RECONSTRUCTION and it is stated as one: the wording's preferred
    /// width plus the prefab's OWN horizontal label inset, floored at the authored width so a
    /// one-word option keeps the shape the artist drew. It is close, not exact, and the difference
    /// is a few pixels of padding — worth naming rather than leaving for someone to measure.</para>
    /// </summary>
    /// <param name="need">OUT: the wording's own unconstrained preferred width, or 0 when it could
    /// not be measured. <see cref="LayoutCaption"/> uses it as the FLOOR for the caption box, so the
    /// two cannot disagree about how wide the text is; the caller prints it.</param>
    /// <param name="inset">OUT: the prefab's own horizontal label inset (0 when the label is not
    /// stretch-anchored, or when there is no label). Handed on for the same reason.</param>
    private static float FitWidth(float authoredWidth, RectTransform rect, TMP_Text? label,
                                  out float need, out float inset)
    {
        need = 0f;
        inset = 0f;
        // The label's own inset inside the button, measured off the prefab: for a stretch-anchored
        // label that is its left+right offsets, which is exactly the padding the artist authored.
        // Resolved BEFORE the layout branch below returns, because the caption box needs it either
        // way — it used to be computed only on the reconstruction path.
        if (label != null && label.rectTransform != null)
        {
            Vector2 min = label.rectTransform.offsetMin;
            Vector2 max = label.rectTransform.offsetMax;
            inset = Mathf.Max(0f, min.x) + Mathf.Max(0f, -max.x);
        }
        if (label != null)
        {
            try
            {
                need = label.GetPreferredValues(label.text).x;
            }
            catch (System.Exception)
            {
                need = 0f;
            }
        }
        try
        {
            float byLayout = LayoutUtility.GetPreferredWidth(rect);
            if (byLayout > 0f)
                return byLayout;
        }
        catch (System.Exception)
        {
            // No usable layout element on this prefab — fall through to the reconstruction.
        }
        if (label == null || !(need > 0f))
            return authoredWidth > 0f ? authoredWidth : 120f;
        float fitted = need + inset * 2f;
        return Mathf.Max(authoredWidth, fitted);
    }

    /// <summary>The option's <c>Selectable</c>: the prefab's <c>InputButton.ExtendedButton</c> when
    /// it has one (the shape <c>DialogPopup</c> itself uses), else any Selectable in the copy. Both
    /// are REFERENCE reads on a serialized field — no Awake has run and none is needed.</summary>
    private static Selectable? ResolveButton(GameObject instance)
    {
        var input = instance.GetComponentInChildren<Script.GUI.Popups.InputButton>(includeInactive: true);
        if (input != null && input.ExtendedButton != null)
            return input.ExtendedButton;
        return instance.GetComponentInChildren<Selectable>(includeInactive: true);
    }

    /// <summary>The button's label: <c>ExtendedButton.buttonText</c> when the prefab has it (the
    /// field <c>DialogPopup.Show</c> writes the option text into), else the first TMP text under the
    /// button — the same fallback order the sender's own sampler walks.</summary>
    private static TMP_Text? ResolveLabel(Selectable button)
    {
        if (button is ExtendedButton extended && extended.buttonText != null)
            return extended.buttonText;
        return button.GetComponentInChildren<TMP_Text>(includeInactive: true);
    }

    private void Clear()
    {
        _buttons.Clear();
        _builtFor = System.Array.Empty<string>();
        if (_row != null)
        {
            // DestroyImmediate, for the mirror's reason: a deferred Destroy would leave the old row
            // alive as a source for the rest of the frame a rebuild lands on.
            Object.DestroyImmediate(_row.gameObject);
            _row = null;
        }
    }
}

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
            widths[_buttons.Count] = FitWidth(authoredW, rect, label);
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

        _row = row;
        _builtFor = new string[built];
        for (int i = 0; i < built; i++)
            _builtFor[i] = lines[i];

        VRLog.Info("Net", $"Remote dialog options: built {built} button(s) from THIS client's own " +
                          $"DialogPopup.optionButtonPrefab — widths from the game's own " +
                          $"LayoutUtility where the prefab provides one (else the stated " +
                          $"reconstruction), fitted to the owner's wordings, " +
                          $"gap {gap:F1} read from the game's own HorizontalLayoutGroup, row " +
                          $"{total:F0}x{height:F0} canvas units. The prefab copies live under a " +
                          "permanently INACTIVE holder, so no Awake ran on any of them; the mirror " +
                          "clones this row and strips it again. Nothing here is ever shown.");
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
    private static float FitWidth(float authoredWidth, RectTransform rect, TMP_Text? label)
    {
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
        if (label == null)
            return authoredWidth > 0f ? authoredWidth : 120f;
        float preferred;
        try
        {
            preferred = label.GetPreferredValues(label.text).x;
        }
        catch (System.Exception)
        {
            return authoredWidth > 0f ? authoredWidth : 120f;
        }
        // The label's own inset inside the button, measured off the prefab: for a stretch-anchored
        // label that is its left+right offsets, which is exactly the padding the artist authored.
        float inset = 0f;
        if (label.rectTransform != null)
        {
            Vector2 min = label.rectTransform.offsetMin;
            Vector2 max = label.rectTransform.offsetMax;
            inset = Mathf.Max(0f, min.x) + Mathf.Max(0f, -max.x);
        }
        float fitted = preferred + inset * 2f;
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

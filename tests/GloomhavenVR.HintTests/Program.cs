using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using GLOO.Introduction;
using GloomhavenVR.WorldUI;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    {
        _checks++;
        if (!value) throw new Exception(reason);
    }

    public static void Main()
    {
        var first = new UIIntroduceBase { name = "Canvas introduction", process = new UIIntroduceProcessHighlight() };
        var second = new UIIntroduceBase { name = "Other introduction", process = new UIIntroduceProcessHighlight() };
        var screen = new UIPartyCharacterAbilityCardsDisplay { introduction = first };
        UnityEngine.Object.Objects.Add(screen);

        var previous = HintMessageOrigins.Begin(first);
        var a = new object();
        HintMessageOrigins.Record(a);
        Check(ReferenceEquals(HintMessageOrigins.For(a)?.Anchor, screen.transform), "Serialized character owner must win over producer canvas ancestry");
        HintMessageOrigins.CurrentScope = previous;

        previous = HintMessageOrigins.Begin(second);
        var b = new object();
        HintMessageOrigins.Record(b);
        HintMessageOrigins.CurrentScope = previous;
        Check(ReferenceEquals(HintMessageOrigins.For(a)?.Anchor, screen.transform), "Enqueuing another producer must not retarget an earlier message");
        Check(ReferenceEquals(HintMessageOrigins.For(b)?.Anchor, second.transform), "Queued messages must retain distinct owners");

        // Native highlight chains enqueue their next message from a later promise callback.
        previous = HintMessageOrigins.Begin(second);
        var nested = HintMessageOrigins.Begin(first.process);
        var next = new object();
        HintMessageOrigins.Record(next);
        Check(ReferenceEquals(HintMessageOrigins.For(next)?.Anchor, screen.transform), "Asynchronous next step must use its own process owner");
        HintMessageOrigins.CurrentScope = nested;
        var afterNested = new object();
        HintMessageOrigins.Record(afterNested);
        Check(ReferenceEquals(HintMessageOrigins.For(afterNested)?.Anchor, second.transform), "Nested process must restore outer scope");

        // Invoke the production Harmony hooks, including exception-preserving unwind.
        object?[] args = { null };
        typeof(HintDirectConceptScopePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        var direct = new object();
        HintMessageOrigins.Record(direct);
        Check(HintMessageOrigins.For(direct)?.Anchor == null, "Direct concept cannot inherit an unrelated completion callback's owner");
        var exception = new InvalidOperationException("native failure");
        object? result = typeof(HintDirectConceptScopePatch).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { exception, args[0] });
        Check(ReferenceEquals(result, exception), "Native exceptions must pass through unchanged");
        Check(ReferenceEquals(HintMessageOrigins.CurrentScope, nested), "Exception unwind must restore previous producer scope");
        HintMessageOrigins.CurrentScope = previous;

        WeakReference discarded = RecordDiscardedMessage();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check(!discarded.IsAlive, "Dismissed message provenance must not keep queue objects alive");
        HintMessageOrigins.Reset();
        Check(HintMessageOrigins.For(a) == null && HintMessageOrigins.CurrentScope == null, "Reset must clear prior scene message references");

        UnityEngine.Object.Objects.Add(new UIPerksWindow { introduction = first });
        previous = HintMessageOrigins.Begin(first);
        var ambiguous = new object();
        HintMessageOrigins.Record(ambiguous);
        Check(ReferenceEquals(HintMessageOrigins.For(ambiguous)?.Anchor, first.transform), "Two serialized owners must not pick whichever type was scanned first");
        HintMessageOrigins.CurrentScope = previous;

        var rewardProducer = new UIIntroductionRewardsProcess();
        var rewardWindow = new UnityEngine.Component();
        UnityEngine.Object.Objects.Add(new CampaignRewardsManager { introductionProcess = rewardProducer, rewardsWindow = rewardWindow });
        previous = HintMessageOrigins.Begin(rewardProducer);
        var rewardHint = new object();
        HintMessageOrigins.Record(rewardHint);
        HintMessageOrigins.CurrentScope = previous;
        Check(ReferenceEquals(HintMessageOrigins.For(rewardHint)?.Anchor, rewardWindow.transform), "Reward hints bypassing UIIntroduceBase must use the serialized reward window");

        var guildProducer = new UIIntroductionRewardsProcess();
        var guildWindow = new UnityEngine.UI.UIWindow();
        Singleton<UIAdventureRewardsManager>.IsInitialized = true;
        Singleton<UIAdventureRewardsManager>.Instance = new UIGuildmasterAdventureRewardsManager
            { rewardIntroduction = guildProducer, window = guildWindow };
        previous = HintMessageOrigins.Begin(guildProducer);
        var guildHint = new object();
        HintMessageOrigins.Record(guildHint);
        HintMessageOrigins.CurrentScope = previous;
        Check(ReferenceEquals(HintMessageOrigins.For(guildHint)?.Anchor, guildWindow.transform),
            "Guildmaster reward hint must use its exact original adventure reward window");
        previous = HintMessageOrigins.Begin(guildProducer.process);
        var laterGuildHint = new object();
        HintMessageOrigins.Record(laterGuildHint);
        HintMessageOrigins.CurrentScope = previous;
        Check(ReferenceEquals(HintMessageOrigins.For(laterGuildHint)?.Anchor, guildWindow.transform),
            "Asynchronous Guildmaster hint step keeps its serialized native reward owner");
        var unrelatedProducer = new UIIntroductionRewardsProcess();
        previous = HintMessageOrigins.Begin(unrelatedProducer);
        var unrelatedRewardHint = new object();
        HintMessageOrigins.Record(unrelatedRewardHint);
        HintMessageOrigins.CurrentScope = previous;
        Check(ReferenceEquals(HintMessageOrigins.For(unrelatedRewardHint)?.Anchor, unrelatedProducer.transform),
            "An unrelated reward producer cannot inherit Guildmaster reward ownership");
        Singleton<UIAdventureRewardsManager>.IsInitialized = false;

        var group = new LevelMessageUILayoutGroup();
        Singleton<UIIntroductionManager>.IsInitialized = true;
        Singleton<UIIntroductionManager>.Instance.LayoutGroup = group;
        Check(ModalFallback.IsIntroductionWindow(group.window), "Native introduction identity must use the manager's exact serialized group");
        Check(!ModalFallback.IsIntroductionWindow(new UnityEngine.UI.UIWindow()), "Unrelated message windows must not be classified by common component type");

        // Production handover hook against a deterministic native-home/conversion test model.
        var window = new UnityEngine.UI.UIWindow();
        var panel = new ConvertedPanel();
        var grab = new Grab();
        var wp = new WindowPanel { Window = window, Panel = panel, Grab = grab };
        ModalFallback.Converted.Add(wp);
        Check(ModalFallback.ReleaseForComposite(window), "Live standalone hint must be adoptable");
        Check(ModalFallback.Converted.Count == 0 && panel.Released && grab.Destroyed, "Retire capture owner and grab before composite records its home");
        Check(string.Join(",", Events.Trace) == "preroll,cancel,grab,release,prehide,screenbind", "Handover must finish ordered teardown before returning to Park");
        Check(!window.NativeHidden, "Composite transfer must not dismiss the native introduction");
        Events.Trace.Clear();
        wp.UserClosing = true;
        ModalFallback.Converted.Add(wp);
        Check(!ModalFallback.ReleaseForComposite(window), "An actual user-close cannot be stolen by a composite");
        Check(ModalFallback.Converted.Count == 1 && Events.Trace.Count == 0, "Rejected handover must leave pending close untouched");
        ModalFallback.Converted.Clear();
        Events.Trace.Clear();
        var dying = new ConvertedPanel { Target = window.transform };
        dying.OnCancel = () => CanvasConversion.Release(dying);
        CanvasConversion.ActivePanels.Add(dying);
        Check(ModalFallback.ReleaseForComposite(window), "A reopening hint must adopt from a pending standalone dissolve");
        Check(dying.Released && CanvasConversion.ActivePanels.Count == 0, "Pending dissolve must release before the composite records native home");
        Check(dying.ReleaseCount == 1, "Cancel completion must not release a converted target twice");
        var untouched = new ConvertedPanel { Target = new UnityEngine.Transform() };
        CanvasConversion.ActivePanels.Add(untouched);
        var withoutRunner = new ConvertedPanel { Target = window.transform };
        CanvasConversion.ActivePanels.Add(withoutRunner);
        Check(ModalFallback.ReleaseForComposite(window) && withoutRunner.Released, "Live conversion without a runner must also hand back synchronously");
        Check(!untouched.Released && CanvasConversion.ActivePanels.Count == 1, "Composite takeover must not release a different native window");
        Reflow();
        Console.WriteLine($"Hint provenance, transfer and reflow: {_checks} assertions passed.");
    }

    private static void Reflow()
    {
        var box = new UnityEngine.RectTransform { sizeDelta = new UnityEngine.Vector2(844, 40), pivot = new UnityEngine.Vector2(.1f, .9f) };
        var border = AddBorder(box, 820, 43);
        border.anchorMin = border.anchorMax = new UnityEngine.Vector2(1, 0);
        border.pivot = new UnityEngine.Vector2(.8f, .2f);
        border.anchoredPosition3D = new UnityEngine.Vector3(-110, 35, 2);
        var borderFitter = new UnityEngine.UI.ContentSizeFitter();
        border.Components.Add(typeof(UnityEngine.UI.ContentSizeFitter), borderFitter);
        var text = new TMPro.TMP_Text { text = "Klicke auf den ersten Charakterplatz, um den Personalisten zu öffnen.", enableAutoSizing = true };
        text.rectTransform.parent = box;
        text.rectTransform.sizeDelta = new UnityEngine.Vector2(800, 23);
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new UnityEngine.Vector2(0, 1);
        text.rectTransform.pivot = new UnityEngine.Vector2(.2f, .8f);
        text.rectTransform.anchoredPosition3D = new UnityEngine.Vector3(310, -35, -2);
        var fitter = new UnityEngine.UI.ContentSizeFitter();
        var textFitter = new UnityEngine.UI.ContentSizeFitter { enabled = false };
        var layout = new UnityEngine.UI.LayoutGroup();
        box.Components.Add(typeof(UnityEngine.UI.ContentSizeFitter), fitter);
        box.Components.Add(typeof(UnityEngine.UI.LayoutGroup), layout);
        text.transform.Components.Add(typeof(UnityEngine.UI.ContentSizeFitter), textFitter);
        var reflow = new HintTextReflow();
        string originalText = text.text;
        reflow.Apply(text, 280);
        Check(text.enableWordWrapping && !text.enableAutoSizing && text.fontSize == 24, "Narrow hints must wrap at authored font size instead of shrinking glyphs");
        Check(box.rect.width == 280 && text.rectTransform.rect.width == 260, "Native border padding must survive narrow-column reflow");
        Check(text.rectTransform.rect.height > 23 && box.rect.height == text.rectTransform.rect.height + 20, "Wrapped native TMP height must grow its original plate");
        Check(border.rect.width == 280 && border.rect.height == text.rectTransform.rect.height + 20, "The actual native sibling border must follow the reflowed text");
        Check(Contains(border.InParent, text.rectTransform.InParent), "Reflowed text must remain inside its original border despite desktop anchors and offsets");
        Check(border.anchoredPosition3D.z == 2 && text.rectTransform.anchoredPosition3D.z == -2, "Reflow must preserve authored text and plate depth ordering");
        Check(!borderFitter.enabled, "The native border fitter must not overwrite the wrapped height");
        Check(text.text == originalText, "Reflow must preserve native localized content");
        Check(!fitter.enabled && !layout.enabled, "Native single-line layout writers must stand down while parked");
        reflow.Apply(text, 280);
        Check(text.MeshUpdates == 1, "Unchanged parked hints must not rebuild meshes every frame");
        text.text += " Noch eine zusätzliche Zeile.";
        reflow.Apply(text, 280);
        Check(text.MeshUpdates == 2, "Native localization or content change must recompute wrapped geometry");
        reflow.Apply(text, 1920);
        Check(box.rect.width == 820, "Wide owners must not expand hints beyond their native authored width");
        reflow.Restore();
        Check(box.sizeDelta.x == 844 && box.sizeDelta.y == 40 && text.rectTransform.sizeDelta.x == 800 && text.rectTransform.sizeDelta.y == 23, "Unpark must restore both native rects exactly");
        Check(border.anchorMin.x == 1 && border.anchorMax.y == 0 && border.pivot.x == .8f && border.anchoredPosition3D.x == -110 && border.anchoredPosition3D.y == 35 && border.rect.width == 820 && border.rect.height == 43 && borderFitter.enabled, "Unpark must restore the sibling border pose, size and fitter exactly");
        Check(text.rectTransform.anchorMin.x == 0 && text.rectTransform.anchorMax.y == 1 && text.rectTransform.pivot.x == .2f && text.rectTransform.anchoredPosition3D.x == 310 && text.rectTransform.anchoredPosition3D.y == -35, "Unpark must restore the native text anchors, pivot and offsets exactly");
        Check(!text.enableWordWrapping && text.enableAutoSizing && text.fontSize == 24, "Unpark must restore native wrapping, sizing and font settings");
        Check(fitter.enabled && layout.enabled && !textFitter.enabled, "Unpark must restore each original layout writer state");
        reflow.Apply(text, 280);
        var nextBox = new UnityEngine.RectTransform { sizeDelta = new UnityEngine.Vector2(600, 60) };
        AddBorder(nextBox, 600, 60);
        var nextText = new TMPro.TMP_Text { text = "Next message" };
        nextText.rectTransform.parent = nextBox;
        nextText.rectTransform.sizeDelta = new UnityEngine.Vector2(560, 30);
        reflow.Apply(nextText, 300);
        Check(box.rect.width == 844 && fitter.enabled, "Switching native text instances must restore the preceding message");
        reflow.Restore();
        Check(nextBox.rect.width == 600 && nextText.rectTransform.rect.width == 560, "New message restore must use its own native layout snapshot");
        var unknownBox = new UnityEngine.RectTransform { sizeDelta = new UnityEngine.Vector2(900, 50) };
        var unknownText = new TMPro.TMP_Text { text = "Unsupported native hierarchy" };
        unknownText.rectTransform.parent = unknownBox;
        unknownText.rectTransform.sizeDelta = new UnityEngine.Vector2(850, 30);
        reflow.Apply(unknownText, 280);
        Check(unknownText.MeshUpdates == 0 && unknownBox.rect.width == 900, "Unknown native layouts must remain untouched instead of resizing an arbitrary parent");
    }

    private static UnityEngine.RectTransform AddBorder(UnityEngine.RectTransform box, float width, float height)
    {
        var border = new UnityEngine.RectTransform { parent = box, sizeDelta = new UnityEngine.Vector2(width, height) };
        border.Components.Add(typeof(UnityEngine.UI.Image), new UnityEngine.UI.Image());
        box.Children.Add("BG", border);
        return border;
    }

    private static bool Contains(UnityEngine.Rect outside, UnityEngine.Rect inside) =>
        outside.xMin <= inside.xMin && outside.yMin <= inside.yMin && outside.xMax >= inside.xMax && outside.yMax >= inside.yMax;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RecordDiscardedMessage()
    {
        var message = new object();
        HintMessageOrigins.Record(message);
        return new WeakReference(message);
    }
}

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
        Console.WriteLine($"Hint provenance and transfer: {_checks} assertions passed.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RecordDiscardedMessage()
    {
        var message = new object();
        HintMessageOrigins.Record(message);
        return new WeakReference(message);
    }
}

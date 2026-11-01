using System;
using System.Linq;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class Program
{
    private static int _assertions;

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        BareTransformNegativeControl();
        BuildUpdatePrompt();
        Console.WriteLine($"Self-update dialog production regression harness: {_assertions:N0} assertions passed.");
    }

    private static void BareTransformNegativeControl()
    {
        var bare = new GameObject("Bare uGUI node");
        bool threw = false;
        try
        {
            _ = (RectTransform)bare.transform;
        }
        catch (InvalidCastException)
        {
            threw = true;
        }

        Check(threw, "Negative control: a bare GameObject Transform rejects uGUI layout casts");
    }

    private static void BuildUpdatePrompt()
    {
        GameObject.ClearAll();
        var dialog = new SelfUpdateDialog();
        int ignored = 0;
        int updated = 0;
        dialog.ShowChoice("Update available", "A newer release is ready.", "Ignore", "Update",
            () => ignored++, () => updated++);

        Check(dialog.IsShowing, "Release result builds and shows the update prompt");
        Check(dialog.Mode == SelfUpdateDialogMode.Choice, "New prompt starts in choice mode");
        Check(CanvasConversion.Ordered.Count == 1,
            "Update prompt joins the existing complete world-window draw-order slot");
        var ordered = CanvasConversion.Ordered.Single();
        Check(ReferenceEquals(ordered.Target, ordered.HostRect)
            && ReferenceEquals(ordered.HostGo, ordered.HostCanvas.gameObject)
            && ordered.HostRect.sizeDelta.x == 660f && ordered.HostRect.sizeDelta.y == 560f,
            "Ordering uses the update dialog's actual canvas and complete physical frame");
        foreach (string name in new[] { "Progress", "Track", "Fill" })
        {
            GameObject node = GameObject.All.Single(candidate => candidate.name == name);
            Check(node.transform is RectTransform, $"{name} is constructed as a RectTransform layout node");
        }

        dialog.ShowProgress("Downloading", "Cancel", () => ignored++);
        dialog.SetProgress(0.75f);
        GameObject fill = GameObject.All.Single(candidate => candidate.name == "Fill");
        var fillRect = (RectTransform)fill.transform;
        Check(fillRect.anchorMax.x == 0.75f, "Progress fill accepts a fraction after the prompt is built");
        Check(updated == 0, "Prompt construction does not invoke the update callback");
        dialog.Close();
        Check(!dialog.IsShowing, "Prompt closes after the regression path completes");
        Check(CanvasConversion.Ordered.Count == 0, "Closing the update prompt retires its exact draw-order slot");
        dialog.ShowChoice("Update available", "Again", "Ignore", "Update", () => ignored++, () => updated++);
        dialog.ShowChoice("Update available", "Replacement", "Ignore", "Update", () => ignored++, () => updated++);
        Check(CanvasConversion.Ordered.Count == 1,
            "Replacing an update prompt cannot accumulate stale ordering owners");
        dialog.ShowDone("Finished", "Close", () => ignored++);
        Check(CanvasConversion.Ordered.Count == 1 && dialog.Mode == SelfUpdateDialogMode.Done,
            "Final message retains the same original ordered canvas");
        dialog.Close();
        dialog.Close();
        Check(CanvasConversion.Ordered.Count == 0 && updated == 0,
            "Repeated close is idempotent and never starts an update");
    }
}

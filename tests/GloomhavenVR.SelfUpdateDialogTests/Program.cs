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
    }
}

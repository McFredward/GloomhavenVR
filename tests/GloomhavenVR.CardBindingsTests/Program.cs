using System;
using System.Linq;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static GameObject AddGroup(Transform parent, int index)
    {
        var child = new GameObject("Native group " + index);
        parent.Add(child.transform);
        var group = child.Add<CanvasGroup>();
        group.alpha = index / 100f;
        group.enabled = index % 2 == 0;
        group.ignoreParentGroups = index % 3 == 0;
        child.activeSelf = index % 5 != 0;
        return child;
    }
    private static void CheckCapture(CardAppearanceBindings bindings, int total)
    {
        var legacy = bindings.Capture();
        var extra = bindings.CaptureExtraGroups();
        var groups = legacy.Where(n => n.Role >= 12).Concat(extra).ToArray();
        Check(groups.Length == total, "Every real group survives capture, including groups beyond the wire budget");
        Check(legacy.Count(n => n.Role >= 12) == Math.Min(8, total), "Legacy group roles remain bounded to eight");
        Check(extra.Length == Math.Max(0, total - 8), "Every additional group is handed to the extension");
        Check(extra.All(n => n.Role == 12), "Supplemental roles use binding identity");
        Check(groups.Select(n => n.Binding).Distinct().Count() == total, "No group aliases another group");
        foreach (var node in groups)
        {
            var group = bindings.Groups[node.Binding];
            Check(node.Values[0] == group.alpha, "Group opacity survives without normalization");
            Check(node.Flags == ((group.gameObject.activeSelf ? 1 : 0) | (group.enabled ? 2 : 0)
                | (group.ignoreParentGroups ? 4 : 0)), "All original group flags survive");
        }
    }
    private static void Main()
    {
        foreach (int count in new[] { 9, 64, 65 })
        {
            var root = new GameObject("Full");
            var effects = root.Add<CardEffects>();
            for (int i = 1; i <= count; i++) AddGroup(root.transform, i);
            // This executes the production constructor that previously threw at the ninth group.
            var bindings = new CardAppearanceBindings(effects);
            CheckCapture(bindings, count);
            var dynamic = AddGroup(root.transform, count + 1);
            CheckCapture(bindings, count + 1);
            root.transform.Children.Remove(dynamic.transform);
            CheckCapture(bindings, count);
        }
        var full = new GameObject("Full");
        var native = full.Add<CardEffects>();
        var groupRoot = full.Add<CanvasGroup>();
        groupRoot.alpha = .25f;
        var holder = new GameObject("Flame holder"); full.transform.Add(holder.transform);
        var image = new GameObject("Flame"); holder.transform.Add(image.transform);
        native._uiFxOverlay = image.Add<Graphic>();
        holder.activeSelf = false;
        var hierarchy = new CardAppearanceBindings(native);
        Check((hierarchy.Capture().Single(n => n.Role == 11).Flags & 1) == 0,
            "Disabled intermediate native holder hides its otherwise active graphic");
        holder.activeSelf = true;
        Check((hierarchy.Capture().Single(n => n.Role == 11).Flags & 1) != 0,
            "Reactivated intermediate holder restores its graphic");
        full.activeSelf = false;
        var detached = hierarchy.Capture(detachedRoot: true);
        Check((detached.Single(n => n.Role == 11).Flags & 1) != 0,
            "Detached local flight ignores only the parked original root");
        Check(detached.Single(n => n.Role >= 12).Values[0] == 1f,
            "Detached root opacity belongs to the flight");
        Console.WriteLine($"Card binding runtime harness: {_assertions} assertions passed.");
    }
}

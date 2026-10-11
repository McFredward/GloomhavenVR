using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static IEnumerator Topology660()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 1;
        Transform source = Go("Actual owner frame").transform, observer = Go("Actual observer frame").transform;
        observer.position = Vector3.right * 5;
        var panel = Rect("Original button panel", source, Vector2.zero, new Vector2(300, 100));
        panel.localScale = Vector3.one * .01f;
        panel.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Image ink = Image("Original button ink", panel, Vector2.zero, new Vector2(240, 65), Color.cyan);
        panel.gameObject.AddComponent<GameplayFixture>();
        const string address = "item.confirm.part.660|";
        TownServiceMirror.RegisterTemplate(1, 1, panel, address: address);
        TownServiceMirror.BeginSession(1, 660, source, source);
        TownServiceMirror.RegisterModule(10, 1, panel, address: address);
        yield return null; Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (float until = Time.unscaledTime + .15f; Time.unscaledTime < until;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        var previous = Remote(1, 10)!;
        Check(previous != null && previous.Root.gameObject.activeInHierarchy,
            "original merchant button picture is initially visible");
        Color32[] originalPixels = Render(previous.Root, 29, "initial-observer");
        uint before = previous.Structure;

        // A genuine live engine hierarchy revision, not a rewritten DTO hash.
        var masked = Rect("New actual mask", panel, new Vector2(40, -20), new Vector2(45, 20));
        Image maskImage = masked.gameObject.AddComponent<Image>(); maskImage.color = Color.white;
        Mask mask = masked.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
        Image added = Image("New actual live child", masked, Vector2.zero, new Vector2(35, 10), Color.yellow);
        Button sourceButton = added.gameObject.AddComponent<Button>();
        int callbacks = 0; sourceButton.onClick.AddListener(() => callbacks++);
        added.gameObject.AddComponent<GameplayFixture>();
        yield return null;
        List<byte[]> revision = Capture(); TownServiceFrame changed = Module660(revision);
        Check(changed.Visible && changed.Structure != before && changed.BaseSequence == 0,
            "actual capture rebuilds changed live topology and publishes a complete same-address original");
        Receive(1, revision); TownServiceMirror.TickRemote(_ => observer);
        Check(ink.gameObject.activeInHierarchy && ink.enabled && added.gameObject.activeInHierarchy,
            "owner original button and new live child both remain visible");
        for (int render = 0; render < 5; render++)
        {
            var held = Remote(1, 10)!;
            Check(held == previous && held.Root.gameObject.activeInHierarchy,
                "pending same-identity native replacement preserves the continuously visible validated button panel");
            Check(Render(held.Root, 29, "cold-" + render).SequenceEqual(originalPixels),
                "unprepared topology never modifies any previously validated button pixels");
            TownServiceMirror.TickRemote(_ => observer); yield return null;
        }

        // The native source boundary supplies a real matching original after its
        // previous frozen asset lifetime ends. RegisterTemplate itself creates
        // the inactive neutralized bank; no hierarchy is synthesized from a DTO.
        var templates = (Dictionary<string, GameObject>)typeof(TownServiceMirror)
            .GetField("Templates", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        GameObject frozen = templates.Single(pair => pair.Key.EndsWith(address, StringComparison.Ordinal)).Value;
        Object.DestroyImmediate(frozen);
        int awakes = GameplayFixture.Awakes, enables = GameplayFixture.Enables, resolutions = 0;
        TownServiceMirror.ResolveTemplate = (service, template, wanted) =>
        {
            if (wanted != address) return false;
            resolutions++;
            TownServiceMirror.RegisterTemplate(service, template, panel, address: wanted);
            return true;
        };
        for (float until = Time.unscaledTime + .35f; Time.unscaledTime < until;)
        {
            TownServiceMirror.TickRemote(_ => observer);
            var displayed = Remote(1, 10)!;
            Check(displayed != null && displayed.Root.gameObject.activeInHierarchy,
                "prepared native exchange has no rendered inactive gap");
            Check(Render(displayed.Root, 29, "exchange-" + _assertions).Any(pixel => pixel.g > 200),
                "prepared native exchange retains button ink on every checked render");
            yield return null;
        }
        var replacement = Remote(1, 10)!;
        Check(replacement != previous && replacement.Structure == changed.Structure && resolutions == 1,
            "prepared exact native original replaces the previous topology rather than retaining stale buttons");
        Check(replacement.Root.GetComponentsInChildren<GameplayFixture>(true).Length == 0
            && replacement.Root.GetComponentsInChildren<Button>(true).Length == 0
            && GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables && callbacks == 0,
            "new native topology retains masks without gameplay controllers or callbacks");
        Check(replacement.Root.GetComponentsInChildren<Mask>(true).Length == 1,
            "replacement preserves the owner's new native mask component");
        ComparePixels(panel, replacement.Root, "complete-native-replacement");

        // Reusing this module number for a different original identity may not
        // expose the old cyan artwork while the owner displays magenta content.
        const string changedIdentity = "item.confirm.part.661|";
        ink.color = Color.magenta;
        TownServiceMirror.RegisterTemplate(1, 1, panel, address: changedIdentity);
        TownServiceMirror.RegisterModule(10, 1, panel, address: changedIdentity);
        List<byte[]> differentOriginal = Capture();
        Object.DestroyImmediate(templates.Single(pair => pair.Key.EndsWith(changedIdentity, StringComparison.Ordinal)).Value);
        Receive(1, differentOriginal); TownServiceMirror.TickRemote(_ => observer);
        Check(!replacement.Root.gameObject.activeInHierarchy && ink.gameObject.activeInHierarchy,
            "a changed original identity never retains preceding visible artwork during preparation");
        TownServiceMirror.RegisterTemplate(1, 1, panel, address: changedIdentity);
        for (float until = Time.unscaledTime + .35f; Time.unscaledTime < until;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        replacement = Remote(1, 10)!;
        Check(replacement != null && replacement.Root.gameObject.activeInHierarchy,
            "a changed original identity appears only after its exact native source is prepared");
        ComparePixels(panel, replacement.Root, "changed-original-identity");

        // A second genuine revision is deliberately cold. Withdrawal must skip
        // the replacement retry delay, including within this same render tick.
        Image("Another actual live child", panel, new Vector2(-40, 20), new Vector2(15, 5), Color.green);
        yield return null; Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(1, 10) == replacement && replacement.Root.gameObject.activeInHierarchy,
            "a later cold topology revision retains only its same original identity");
        panel.gameObject.SetActive(false);
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        Check(!replacement.Root.gameObject.activeInHierarchy,
            "owner withdrawal during native preparation hides immediately before the retry clock");
        TownServiceMirror.UnregisterModule(10);
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(1, 10) == null, "genuine module removal retires a pending native replacement immediately");

        // A different source identity cannot display the preceding card's art.
        panel.gameObject.SetActive(true);
        TownServiceMirror.RegisterModule(11, 1, panel, address: changedIdentity);
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(1, 11) == null,
            "new module with no matching native topology cannot display guessed artwork");
        TownServiceMirror.EndSession(); Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        Check(!TownServiceMirror.RemoteSessions[1].Active,
            "owner session closure remains immediate while an original is pending");
        TownServiceMirror.ResolveTemplate = null;
        File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\n");
    }

    private static TownServiceFrame Module660(List<byte[]> packets)
    {
        foreach (byte[] bytes in packets)
            if (TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame) && frame!.Module == 10)
                return frame;
        throw new InvalidOperationException("Actual owner capture omitted the changed module.");
    }
}

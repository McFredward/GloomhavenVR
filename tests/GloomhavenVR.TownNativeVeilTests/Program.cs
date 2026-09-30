using System;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;

static class Program
{
    private static int _checks;
    private static void Check(bool condition, string reason)
    { _checks++; if (!condition) throw new Exception(reason); }

    private static void Main()
    {
        // The actual four map prefabs already carry a root CanvasGroup. Native
        // animation may update its alpha after presentation Tick, before render.
        var source = new GameObject().transform;
        var native = source.gameObject.AddComponent<CanvasGroup>();
        native.alpha = .4f;
        var nativeLayout = source.gameObject.AddComponent<LayoutElement>();
        var child = new GameObject(); child.transform.SetParent(source);
        var independent = child.AddComponent<CanvasGroup>();
        independent.ignoreParentGroups = true;
        var display = source.gameObject.AddComponent<UIPartyCharacterEnhancementAbilityCardsDisplay>();
        display.slotsPool.Add(new object());

        var veil = new TownServiceNativeListVeil(source);
        Check(source.GetComponents<CanvasGroup>().Length == 1, "existing CanvasGroup reused, never duplicated");
        Check(native.alpha == 0f && native.interactable && !native.blocksRaycasts
              && native.ignoreParentGroups, "native selection retained while flat pixels hidden");
        Check(nativeLayout.ignoreLayout && !independent.ignoreParentGroups, "layout and child override masked");
        native.alpha = .8f;
        Canvas.Render();
        Check(native.alpha == 0f, "late native alpha write cannot flash before canvas render");
        veil.Dispose();
        Check(native.alpha == .8f && native.blocksRaycasts && !native.ignoreParentGroups
              && native.interactable && native.enabled, "same-frame fallback restores updated native group");
        Check(!nativeLayout.ignoreLayout && nativeLayout.enabled && independent.ignoreParentGroups,
              "same-frame fallback restores every native layout and override");

        // Reopen before Unity processes Destroy: the same native component remains valid.
        var reopened = new TownServiceNativeListVeil(source);
        Check(source.GetComponents<CanvasGroup>().Length == 1 && native.alpha == 0f,
              "pooled same-frame reopen reuses existing group");
        reopened.Dispose();
        Check(native.alpha == .8f, "second close restores native alpha");

        // A prefab without a group receives one owned gate. Disposal deactivates it
        // immediately and a same-frame reopen reuses it instead of Destroying it.
        var bare = new GameObject().transform;
        var first = new TownServiceNativeListVeil(bare);
        var owned = bare.GetComponent<CanvasGroup>()!;
        first.Dispose();
        Check(!owned.enabled && owned.alpha == 1f && owned.blocksRaycasts,
              "owned gate becomes inert before the deferred-destroy frame");
        var second = new TownServiceNativeListVeil(bare);
        Check(ReferenceEquals(owned, bare.GetComponent<CanvasGroup>()) && owned.enabled && owned.alpha == 0f,
              "same-frame reopen reuses owned gate");
        second.Dispose();
        Check(!owned.enabled, "owned gate inert after repeat close");
        Console.WriteLine($"Town native veil: {_checks} assertions.");
    }
}

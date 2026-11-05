using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class SceneSymbol761
{
    public static int Checks;
    private static readonly List<GameObject> Owned = new();
    private static void Check(bool condition, string label)
    { Checks++; if (!condition) throw new Exception(label); }
    private static GameObject Root(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.SetActive(false); Owned.Add(go); return go;
    }
    private static RectTransform Child(GameObject parent)
    {
        var go = Root("Original symbol child"); go.transform.SetParent(parent.transform, false);
        return (RectTransform)go.transform;
    }
    private static void RepeatedSceneSymbol<T>(string name) where T : Component
    {
        var native = Root(name); native.AddComponent<T>();
        RectTransform target = Child(native);
        Check(MrBackingSceneSymbols.Contains(target), "actual native scene symbol classified: " + name);
        var panel = new ConvertedPanel { Target = target };
        for (int i = 0; i < 8; i++)
        {
            // The converter's optional flag is deliberately absent on every pooled
            // adoption. Execute the exact production retirement/admission expressions.
            panel.MrBackingSuppressed = false;
            Check(BackingGate761.Retire(true, panel), "scene symbol retires an existing readability plate: " + name);
            Check(BackingGate761.Refuse(panel.HostRect, panel), "scene symbol cannot acquire new readability geometry: " + name);
        }
        var plate = Root("Previously registered MR plate");
        if (BackingGate761.Retire(true, panel)) UnityEngine.Object.DestroyImmediate(plate);
        Check(plate == null, "actual prior plate geometry removed: " + name);
    }
    public static int Run()
    {
        Checks = 0;
        try
        {
            Check(typeof(WorldspacePanelUIController).Assembly.GetName().Name == "GH.Runtime", "original game actor controller types bind");
            RepeatedSceneSymbol<WorldspacePanelUIController>("Native actor health/status root");
            RepeatedSceneSymbol<WorldspaceUI.AttackModBar>("Native attack modifier bar");
            RepeatedSceneSymbol<AttackModElementUI>("Native pooled attack modifier element");
            var ordinary = Root("Ordinary UI window");
            var panel = new ConvertedPanel { Target = (RectTransform)ordinary.transform };
            Check(!MrBackingSceneSymbols.Contains(panel.Target), "ordinary UI remains eligible");
            Check(!BackingGate761.Retire(true, panel), "ordinary UI keeps its existing plate");
            Check(!BackingGate761.Refuse(panel.HostRect, panel), "ordinary UI can build its plate");
            var nativeDescendant = Root("Unrelated nested actor symbol");
            nativeDescendant.transform.SetParent(ordinary.transform, false);
            nativeDescendant.AddComponent<AttackModElementUI>();
            Check(!MrBackingSceneSymbols.Contains(panel.Target), "a descendant symbol cannot deny its containing UI window");
            var nativeUiCard = Root("Native modifier card in a genuine UI");
            nativeUiCard.transform.SetParent(ordinary.transform, false);
            nativeUiCard.AddComponent<AttackModifierCardGUI>();
            Check(!MrBackingSceneSymbols.Contains(nativeUiCard.transform), "native UI modifier cards keep ordinary UI eligibility");
            panel.MrBackingSuppressed = true;
            Check(BackingGate761.Retire(true, panel) && BackingGate761.Refuse(panel.HostRect, panel), "existing explicit opt-out remains authoritative");
            Check(BackingGate761.Retire(false, panel), "released panels retire");
            Check(!MrBackingSceneSymbols.Contains(null), "null native target does not invent ownership");
            var destroyed = Root("Destroyed target"); var transform = destroyed.transform;
            UnityEngine.Object.DestroyImmediate(destroyed);
            Check(!MrBackingSceneSymbols.Contains(transform), "destroyed native target does not retain ownership");
            return Checks;
        }
        finally
        {
            for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) UnityEngine.Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
        }
    }
}

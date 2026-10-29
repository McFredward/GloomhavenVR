using System;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.UI;

internal static class CaptureBoundaryCases745
{
    internal static void Run()
    {
        foreach (int scene in new[] { 1, 2 })
        foreach (Func<GameObject, Component> create in new Func<GameObject, Component>[] {
            go => go.AddComponent<UILevelUpWindow>(), go => go.AddComponent<UIPerksInventory>(),
            go => go.AddComponent<UIPartyItemInventoryDisplay>() })
        foreach (bool itemPrompt in new[] { false, true })
        {
            CanvasConversion.Reset(); WorldUIConfig.ConversionActive = true;
            WorldUIConfig.Dialogs.Value = VRModeStateMachine.TableInFrontOfPlayer = true;
            FlatScreen.ManualScreenActive = false;
            var root = new GameObject("capture boundary original owner");
            var panel = CanvasConversion.Convert((RectTransform)root.transform, "owner", 1)!;
            var sourceRoot = new GameObject("exact native producer"); sourceRoot.transform.SetParent(panel.Target, false);
            sourceRoot.AddComponent<UIWindow>(); Component source = create(sourceRoot);
            source.transform.position = new Vector3(43, 18, -7); source.transform.localScale = new Vector3(.6f, .8f, .9f);
            var home = new GameObject("original pooled prompt home").transform;
            var manager = new UIConfirmationBoxManager(); manager.CurrentBox.transform.SetParent(home, false);
            Singleton<UIConfirmationBoxManager>.Instance = manager;
            var itemRoot = new GameObject("original native item confirmation"); itemRoot.transform.SetParent(home, false);
            var item = itemRoot.AddComponent<UIItemConfirmationBox>(); var itemWindow = itemRoot.AddComponent<UIWindow>();
            itemWindow.IsVisible = item.IsActive = false;
            Singleton<UIItemConfirmationBox>.Instance = item;
            Component box = itemPrompt ? item : manager.CurrentBox;
            box.gameObject.scene = scene;
            var rect = (RectTransform)box.transform; rect.sizeDelta = new Vector2(1200, 900);
            rect.localPosition = new Vector3(81, 23, 9); rect.localScale = new Vector3(.8f, .9f, 1);
            rect.localRotation = new Quaternion { w = .7f };
            var surface = new DialogSurface(); surface.Tick();

            var previous = ConfirmationOnOwner.Begin(source);
            if (itemPrompt) ConfirmationOnOwner.Record(item); else ConfirmationOnOwner.Record(manager);
            ConfirmationOnOwner.CurrentScope = previous;
            if (itemPrompt) item.IsActive = itemWindow.IsVisible = true; else manager.Show();
            surface.Tick();

            Program.Check(PanelSupersample.Bindings.Count == 1
                && ReferenceEquals(PanelSupersample.Bindings[0].Owner, panel)
                && ReferenceEquals(PanelSupersample.Bindings[0].Member, CanvasConversion.Last)
                && ReferenceEquals(PanelSupersample.Bindings[0].Member.Target, rect),
                "producer capture boundary receives the exact owner and native member once");
            PanelSupersample.Binding binding = PanelSupersample.Bindings[0];
            Program.Check(binding.MemberRegistered && ReferenceEquals(binding.TargetParent, binding.Member.HostRect)
                && binding.Position.x == 43 && binding.Position.y == 18 && binding.Position.z == -39
                && binding.Scale.x == .6f && binding.Scale.y == .8f && binding.Scale.z == .9f
                && binding.TargetPosition.x == 0 && binding.TargetPosition.y == 0 && binding.TargetPosition.z == 0
                && binding.TargetScale.x == 1 && binding.TargetScale.y == 1 && binding.TargetScale.z == 1
                && binding.TargetRotation.w == 1,
                "producer capture boundary observes completed conversion and final native member placement");
            Program.Check(box.gameObject.scene == scene && !rect.IsChildOf(panel.Target)
                && rect.sizeDelta.x == 1200 && rect.sizeDelta.y == 900,
                "capture handoff retains original prompt geometry and independent scene lifetime");
            for (int frame = 0; frame < 8; frame++) surface.Tick();
            Program.Check(PanelSupersample.Bindings.Count == 1,
                "settled native confirmation never repeats its capture boundary enrollment");
            if (itemPrompt) item.IsActive = itemWindow.IsVisible = false; else manager.Hide();
            surface.Tick();
            Program.Check(ReferenceEquals(rect.parent, home) && !CanvasConversion.ActivePanels.Contains(binding.Member)
                && PanelSupersample.Bindings.Count == 1,
                "native closure retires its conversion without issuing a replacement capture handoff");
            surface.Shutdown();
        }
        CanvasConversion.Reset();
    }
}

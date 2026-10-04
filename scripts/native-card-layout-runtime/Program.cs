using System;
using System.Reflection;
using HarmonyLib;
using GloomhavenVR.Cards;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using UnityEngine;

public static class InteractionProgram
{
    private static int count;
    private static void Check(bool yes, string message) { count++; if (!yes) throw new Exception(message); }
    private static readonly Vector3 RestScale = new(.31f, .31f, .31f);
    private static readonly Vector2 RestPosition = new(17, 19);

    public static int Run()
    {
        count = 0;
        var harmony = new Harmony("ghvr.native-card-layout." + typeof(InteractionProgram).Assembly.GetName().Name);
        var root = new GameObject("ActualVRCard");root.SetActive(false);
        var host = new GameObject("FaceCanvas", typeof(RectTransform));host.transform.SetParent(root.transform, false);
        var faceGo = new GameObject("OriginalFace", typeof(RectTransform));faceGo.SetActive(false);
        var nativeParent = new GameObject("NativeDialog", typeof(RectTransform));nativeParent.SetActive(false);
        try
        {
            var face = faceGo.AddComponent<FullAbilityCard>();
            Check(face.GetType().Assembly.GetName().Name == "GH.Runtime",
                "transform writers use the shipped FullAbilityCard rather than a replacement algorithm");
            Type settingsType = typeof(FullAbilityCard).GetProperty("ViewSettings")!.PropertyType;
            object settings = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(settingsType);
            Set(settingsType, settings, "DefaultScale", new Vector3(.8f, .7f, .6f));
            Set(settingsType, settings, "HoverScale", new Vector3(.9f, .9f, .9f));
            Set(settingsType, settings, "AnotherCardHoveredScale", new Vector3(.4f, .4f, .4f));
            Set(settingsType, settings, "Position", new Vector3(63, 71, 0));
            Set(settingsType, settings, "AnotherCardHoveredPosition", new Vector3(9, 11, 0));
            Set(settingsType, settings, "OverridePosition", true);
            typeof(FullAbilityCard).GetProperty("ViewSettings")!.SetValue(face, settings);
            face.IsActionSelection = true;
            face.transform.SetParent(host.transform, false);
            VRCard owner = root.AddComponent<VRCard>();owner.HasAdoptedFace = true;owner.FullCard = face;
            CardArtGuard.Adopted.Add(face);
            harmony.PatchAll(typeof(FullAbilityCard_UpdateScale_AdoptedLayout));
            harmony.PatchAll(typeof(FullAbilityCard_UpdatePosition_AdoptedLayout));

            Reset(face); View(face, settings);
            Check(AtRest(face), "adopted VR geometry avoids flat transform writes and repair churn");
            Check(ReferenceEquals(face.ViewSettings, settings), "public UpdateView retains the original ViewSettings");
            int hoverEvents = 0;
            Action<bool> hover = _ => hoverEvents++;
            FullAbilityCard.FullCardHoveringStateChanged += hover;
            try
            {
                // The original Highlight checks top/bottom controls after publishing the event;
                // use real inactive original controls rather than suppressing that body.
                face.topActionButton = InactiveAction("OriginalTop", face.transform);
                face.bottomActionButton = InactiveAction("OriginalBottom", face.transform);
                face.Highlight(true);face.Highlight(false);
                Check(hoverEvents == 2, "original hover events are preserved by private-only geometry suppression");
            }
            finally { FullAbilityCard.FullCardHoveringStateChanged -= hover; }

            // Stale registry, returned face, dialog handoff, clones and mode changes all retain
            // the original geometry. No deferred continuation or lost native writer is allowed.
            face.transform.SetParent(nativeParent.transform, false);Reset(face);View(face, settings);
            Check(!AtRest(face), "stale adopted registry yields native dialog geometry immediately");
            face.transform.SetParent(host.transform, false);owner.FullCard = null;Reset(face);View(face, settings);
            Check(!AtRest(face), "mismatched original face ownership retains native geometry");
            owner.FullCard = face;owner.HasAdoptedFace = false;Reset(face);View(face, settings);
            Check(!AtRest(face), "restored or yielded ownership retains native geometry");
            owner.HasAdoptedFace = true;CardArtGuard.Adopted.Remove(face);Reset(face);View(face, settings);
            Check(!AtRest(face), "unregistered original clone retains native geometry");
            CardArtGuard.Adopted.Add(face);VRSession.IsRunning = false;Reset(face);View(face, settings);
            Check(!AtRest(face), "flat and VR-off modes retain native geometry");
            VRSession.IsRunning = true;HandSuppression.Active = false;Reset(face);View(face, settings);
            Check(!AtRest(face), "cards module teardown retains native geometry");
            HandSuppression.Active = true;
            face.transform.SetParent(host.transform, false);Reset(face);View(face, settings);
            Check(AtRest(face), "re-adoption immediately restores the single VR layout writer");
            bool nativeFailure = false;
            try { View(face, null!); }
            catch(TargetInvocationException error){nativeFailure = error.InnerException is NullReferenceException;}
            Check(nativeFailure, "invalid native settings preserve the original failure rather than hiding it");
            typeof(FullAbilityCard).GetProperty("ViewSettings")!.SetValue(face, settings);
            harmony.UnpatchSelf();Reset(face);View(face, settings);
            Check(!AtRest(face), "unpatch restores the exact original transform writers");
            return count;
        }
        finally
        {
            harmony.UnpatchSelf();CardArtGuard.Adopted.Clear();VRSession.IsRunning=true;HandSuppression.Active=true;
            UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(faceGo);
            UnityEngine.Object.DestroyImmediate(nativeParent);
        }
    }
    private static FullAbilityCardAction InactiveAction(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));go.SetActive(false);go.transform.SetParent(parent, false);
        return go.AddComponent<FullAbilityCardAction>();
    }
    private static void Set(Type type, object value, string name, object property)
    {
        FieldInfo? field = type.GetField(name);if(field!=null){field.SetValue(value,property);return;}
        type.GetProperty(name)!.SetValue(value,property);
    }
    private static void View(FullAbilityCard face, object settings)
        => typeof(FullAbilityCard).GetMethod("UpdateView", new[] { typeof(FullAbilityCard).GetProperty("ViewSettings")!.PropertyType })!.Invoke(face, new[] { settings });
    private static void Reset(FullAbilityCard face)
    { face.transform.localScale=RestScale;((RectTransform)face.transform).anchoredPosition=RestPosition; }
    private static bool AtRest(FullAbilityCard face)
        => face.transform.localScale==RestScale&&((RectTransform)face.transform).anchoredPosition==RestPosition;
}

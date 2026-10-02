using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace GloomhavenVR.Quest
{
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "GloomhavenVR Quest passthrough", Company = "GloomhavenVR",
        Desc = "Adds a native passthrough underlay to the existing Unity OpenXR session.",
        Version = "0.1.0", FeatureId = FeatureId,
        OpenxrExtensionStrings = "XR_FB_passthrough",
        BuildTargetGroups = new[] { BuildTargetGroup.Android })]
#endif
    public sealed class QuestPassthroughFeature : OpenXRFeature
    {
        public const string FeatureId = "dev.gloomhavenvr.quest.passthrough";
        const string Library = "ghvr_quest_passthrough";
        public static bool Available { get; private set; }
        static bool instanceReady;
        public static bool Active { get { return Available && ghvr_quest_status() == 2; } }
        [DllImport(Library)] static extern IntPtr ghvr_quest_hook(IntPtr function);
        [DllImport(Library)] static extern int ghvr_quest_instance(ulong instance);
        [DllImport(Library)] static extern int ghvr_quest_session(ulong session);
        [DllImport(Library)] static extern void ghvr_quest_running(int running);
        [DllImport(Library)] static extern int ghvr_quest_enable(int enabled);
        [DllImport(Library)] static extern int ghvr_quest_status();
        [DllImport(Library)] static extern int ghvr_quest_error();
        [DllImport(Library)] static extern void ghvr_quest_destroy_session();
        [DllImport(Library)] static extern void ghvr_quest_destroy_instance();

        protected override IntPtr HookGetInstanceProcAddr(IntPtr function)
        {
            return ghvr_quest_hook(function);
        }
        protected override bool OnInstanceCreate(ulong instance)
        {
            instanceReady = OpenXRRuntime.IsExtensionEnabled("XR_FB_passthrough") && ghvr_quest_instance(instance) == 1;
            Available = false;
            Debug.Log("[GloomhavenVR Quest] passthrough instance available=" + instanceReady);
            // The app can still test ordinary VR if passthrough is unavailable.
            return true;
        }
        protected override void OnSessionCreate(ulong session)
        {
            Available = instanceReady && ghvr_quest_session(session) == 1;
            if (!Available) Debug.LogWarning("[GloomhavenVR Quest] passthrough session unavailable");
        }
        protected override void OnSessionBegin(ulong session)
        {
            if (Available) ghvr_quest_running(1);
        }
        protected override void OnSessionEnd(ulong session)
        {
            if (Available) ghvr_quest_running(0);
        }
        protected override void OnSessionDestroy(ulong session)
        {
            ghvr_quest_destroy_session();
            Available = false;
        }
        protected override void OnInstanceDestroy(ulong instance)
        {
            ghvr_quest_destroy_instance();
            Available = false;
            instanceReady = false;
        }
        public static bool SetEnabled(bool enabled)
        {
            if (!Available) return false;
            bool active = ghvr_quest_enable(enabled ? 1 : 0) == 1;
            Debug.Log("[GloomhavenVR Quest] passthrough requested=" + enabled + " active=" + active + " result=" + ghvr_quest_error());
            return active;
        }
    }
}

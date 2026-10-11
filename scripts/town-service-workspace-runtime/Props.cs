using System;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;

// Async native decoration loading is a fixture; production workspace ownership,
// service furniture choice, source clock copying and visibility remain real.
internal static class WorkspacePropsProgram
{
    internal static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
        NetPlayerActors.Roster.Clear();
        NetPlayerActors.Roster.Add((1, "first", "first")); NetPlayerActors.Roster.Add((7, "second", "second"));
        NetPlayerActors.Local = 7;
        var station = new GameObject("Workspace prop owner");
        var original = GameObject.CreatePrimitive(PrimitiveType.Cube); original.name = "Original offering bowl";
        original.transform.SetParent(station.transform, false); original.transform.localPosition = new Vector3(.1f, 1f, .2f);
        Shader shader = AssetBundle.GetAllLoadedAssetBundles().SelectMany(bundle => bundle.LoadAllAssets<Shader>())
            .First(shader => shader.name == "GloomhavenVR/TownFlame");
        var material = new Material(shader); original.GetComponent<MeshRenderer>().sharedMaterial = material;
        try
        {
            foreach (byte service in new byte[] { 2, 3 })
            {
                TownServiceDecor.Props.Clear(); TownServiceDecor.Props[service] = new() { original.transform };
                using var workspace = new TownServiceWorkspace(station.transform, service);
                workspace.SetVisibility(.6f);
                Check(workspace.FurnitureRoot.name == (service == 1 ? "Counter" : service == 2 ? "Shrine" : "Workbench"),
                    "visitor receives its actual service furniture");
                Check(workspace.Props.Count == 1 && workspace.Props[0].Key == "decor." + service + ".0",
                    "visitor receives complete original static decoration");
                Transform copy = workspace.Props[0].Root;
                Check(copy != original.transform && Vector3.Distance(copy.localPosition, original.transform.localPosition) < .0001f,
                    "original bowl or ledger keeps exact station-local contact pose");
                Material owned = copy.GetComponent<MeshRenderer>().sharedMaterial;
                Check(owned != material, "visitor prop owns its material independently of resident");
                Check(Mathf.Abs(owned.GetFloat("_TownVisibility") - .6f) < .0001f,
                    "static decoration participates in owner workspace dissolve");
                material.SetFloat("_TownAnimationTime", 12.5f); workspace.Tick();
                Check(Mathf.Abs(owned.GetFloat("_TownAnimationTime") - 12.5f) < .0001f,
                    "visitor candle animation follows current resident clock");
                workspace.SetVisibility(0f);
                Check(!copy.gameObject.activeSelf, "hidden workspace has no orphan visible props");
                Check(material.GetFloat("_TownVisibility") == 1f, "visitor fade never changes permanent resident decoration");
            }
        }
        finally
        {
            TownServiceDecor.Props.Clear(); UnityEngine.Object.DestroyImmediate(station);
            UnityEngine.Object.DestroyImmediate(material);
        }
        return assertions;
    }
}

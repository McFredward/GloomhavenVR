using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class InteractionProgram
{
    static int count;
    static void Check(bool yes, string message) { count++; if (!yes) throw new Exception(message); }
    static bool Close(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .0001f;
    static void Roster(params int[] ids)
    { NetPlayerActors.Roster.Clear(); foreach (int id in ids) NetPlayerActors.Roster.Add((id, "account" + id, "user" + id)); }
    public static int Run()
    {
        count = 0; WorkspaceClock.Now = 0;
        var station = new GameObject("Shared station");
        station.transform.position = new Vector3(70, 90, -140);
        station.transform.rotation = Quaternion.Euler(0, 58, 0); station.transform.localScale = Vector3.one * 198;
        var template = TownServiceWorkspace.CounterTemplate!;
        var originals = template.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials).Distinct().ToArray();
        var originalVisibility = originals.Select(m => m.GetFloat("_TownVisibility")).ToArray();
        var workspaces = new List<TownServiceWorkspace>();
        try
        {
            // Connection IDs are not party slots; gaps, flat users and users without an active
            // controllable still occupy distinct complete-roster ordinals.
            Roster(1, 7, 19, 53);
            for (int slot = 0; slot < 4; slot++)
            {
                NetPlayerActors.Local = new[] { 1, 7, 19, 53 }[slot];
                var workspace = new TownServiceWorkspace(station.transform); workspaces.Add(workspace);
                workspace.SetVisibility(1);
                var expected = slot == 0 ? Vector3.zero : new Vector3((slot - 2) * 1.8f, 0, 2.2f);
                Check(Close(workspace.Root.localPosition, expected), "complete roster assigns distinct ordinal including flat peers");
                Check(workspace.FurnitureRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0, "furniture clone contains no NPC");
                Check(workspace.FurnitureRoot.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "decorative furniture cannot intercept input");
                Check(workspace.Materials.All(m => !originals.Contains(m)), "each workspace owns its materials");
                Check(workspace.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - (slot == 0 ? 0 : 1)) < .0001f), "primary duplicate is hidden while extensions are visible");
                Check(workspace.FurnitureRoot.gameObject.activeSelf == (slot != 0), "primary hidden geometry costs no duplicate draw calls");
                foreach (var renderer in workspace.FurnitureRoot.GetComponentsInChildren<MeshRenderer>(true))
                { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); Check(block.isEmpty, "mesh materials are captured without unsupported property blocks"); }
            }
            for (int i = 0; i < originals.Length; i++)
                Check(originals[i].GetFloat("_TownVisibility") == originalVisibility[i], "shared template materials remain unchanged");
            for (int i = 0; i < workspaces.Count; i++)
                for (int j = i + 1; j < workspaces.Count; j++)
                {
                    var a = workspaces[i]; var b = workspaces[j];
                    Check(Vector3.Distance(a.Root.localPosition, b.Root.localPosition) >= 1.79f, "full-size workspace centres remain separated");
                    Check(!a.Materials.Any(m => b.Materials.Contains(m)), "material fades cannot affect another visitor");
                }
            Check(TownServiceWorkspace.Offset(1).z - .422f > .95f + .8f, "extensions leave standing space behind NPC and remain outward of map");
            // Membership changes are not cached; a late joiner and survivor converge from the
            // same complete native roster. Existing owner's transition remains observable.
            NetPlayerActors.Local = 53; var moving = workspaces[3];
            Roster(1, 19, 53); WorkspaceClock.Now = 1; moving.Tick();
            Check(Close(moving.Root.localPosition, new Vector3(1.8f, 0, 2.2f)), "roster change begins from existing owner pose");
            WorkspaceClock.Now = 1.11f; moving.Tick();
            Check(Math.Abs(moving.Root.localPosition.x - .9f) < .002f, "membership changes animate through intermediate owner pose");
            WorkspaceClock.Now = 1.23f; moving.Tick();
            Check(Close(moving.Root.localPosition, new Vector3(0, 0, 2.2f)), "membership transition reaches current ordinal");
            using (var lateJoin = new TownServiceWorkspace(station.transform))
                Check(Close(lateJoin.Root.localPosition, moving.Root.localPosition), "late join and survivor resolve identical target from current full roster");
            Roster(1, 19, 53, 88); NetPlayerActors.Local = 88;
            using (var reconnect = new TownServiceWorkspace(station.transform))
                Check(Close(reconnect.Root.localPosition, new Vector3(1.8f, 0, 2.2f)), "new connection ID resolves next distinct seat");
            NetPlayerActors.Local = 1;
            workspaces[0].Tick(); Check(workspaces[0].Root.localPosition == Vector3.zero, "host primary service stays beside actual NPC");
            moving.SetVisibility(.4f);
            Check(moving.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - .4f) < .0001f), "owner material dissolve exposes intermediate value for mirror");
            moving.Dispose(); moving.Dispose();
            Check(!moving.Root.gameObject.activeSelf && moving.Materials.Count == 0, "disposal hides furniture immediately and releases owned materials");
            Roster(1, 19, 53, 88, 104); NetPlayerActors.Local = 104;
            bool rejected = false;
            try { using var excess = new TownServiceWorkspace(station.transform); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("exceeds four users"); }
            Check(rejected, "unexpected fifth user is rejected instead of overlapping a valid seat");
            Roster(); NetPlayerActors.Local = 0;
            using (var solo = new TownServiceWorkspace(station.transform))
            { solo.SetVisibility(1); Check(solo.Root.localPosition == Vector3.zero, "offline keeps original front counter"); }
            return count;
        }
        finally { foreach (var workspace in workspaces) workspace.Dispose(); UnityEngine.Object.Destroy(station); }
    }
}

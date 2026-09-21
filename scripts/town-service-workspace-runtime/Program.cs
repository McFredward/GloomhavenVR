using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

public static class InteractionProgram
{
    private static int count;
    private static void Check(bool yes, string message) { count++; if (!yes) throw new Exception(message); }
    private static bool Close(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .001f;
    private static void Roster(params int[] ids)
    { NetPlayerActors.Roster.Clear(); foreach (int id in ids) NetPlayerActors.Roster.Add((id, "account" + id, "user" + id)); }
    private static Vector3 Expected(int slot)
    {
        float angle = slot == 1 ? -124f : slot == 2 ? 180f : 124f;
        return MapRoomDriver.Center + Quaternion.Euler(0, MapRoomDriver.Yaw + angle, 0) * new Vector3(0, 0, 2.35f * MapRoomDriver.Scale);
    }
    private static void PlaceStation(Transform station)
    {
        Check(TownServicePlacement.TryResolve(1, MapRoomDriver.Center, MapRoomDriver.Scale, out var p, out var q), "actual merchant placement resolves");
        station.SetPositionAndRotation(p, q); station.localScale = Vector3.one * MapRoomDriver.Scale;
    }
    private static Vector2[] Rectangle(Vector3 p, Quaternion q, float x, float zmin, float zmax)
    {
        return new[] { new Vector3(-x, 0, zmin), new Vector3(x, 0, zmin), new Vector3(x, 0, zmax), new Vector3(-x, 0, zmax) }
            .Select(v => p + q * v).Select(v => new Vector2(v.x, v.z)).ToArray();
    }
    private static bool Separated(Vector2[] a, Vector2[] b)
    {
        foreach (var shape in new[] { a, b }) for (int i = 0; i < 2; i++)
        {
            Vector2 edge = shape[i + 1] - shape[i], axis = new Vector2(-edge.y, edge.x).normalized;
            var aa = a.Select(v => Vector2.Dot(v, axis)).ToArray(); var bb = b.Select(v => Vector2.Dot(v, axis)).ToArray();
            if (aa.Max() < bb.Min() - .01f || bb.Max() < aa.Min() - .01f) return true;
        }
        return false;
    }
    private static void CheckGeometry(List<TownServiceWorkspace> workspaces)
    {
        // The complete source-generated station envelopes, not duplicated station roots.
        var fixedStations = new List<Vector2[]>();
        for (byte service = 1; service <= 3; service++)
        {
            Check(TownServicePlacement.TryResolve(service, MapRoomDriver.Center, MapRoomDriver.Scale, out var p, out var q), "actual resident source supplies clearance pose");
            fixedStations.Add(Rectangle((p - MapRoomDriver.Center) / MapRoomDriver.Scale, q, .9f, -.5f, 1.15f));
        }
        var extras = new List<Vector2[]>();
        for (int i = 1; i < workspaces.Count; i++)
        {
            var root = workspaces[i].Root;
            var envelope = Rectangle((root.position - MapRoomDriver.Center) / MapRoomDriver.Scale, root.rotation, .9f, -.422f, .422f);
            foreach (Vector2 corner in envelope)
                Check(corner.magnitude < 3.033f, "full-size counter stays inside solid scenery clearance");
            Vector3 inward = -root.forward;
            Vector3 near = (root.position - MapRoomDriver.Center) / MapRoomDriver.Scale + inward * .422f;
            Check(new Vector2(near.x, near.z).magnitude > 1.05f, "full-size counter stays outside map and player seat ring");
            Check(Vector3.Dot(inward, (MapRoomDriver.Center - root.position).normalized) > .99f, "counter faces map instead of inheriting merchant yaw");
            foreach (var fixedEnvelope in fixedStations) Check(Separated(envelope, fixedEnvelope), "extra counter clears all three actual resident envelopes");
            foreach (var other in extras) Check(Separated(envelope, other), "full-size extra counters cannot overlap");
            extras.Add(envelope);
            // Shipping bundle meshes must fit inside the conservative footprint used above.
            foreach (var filter in workspaces[i].FurnitureRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds b = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var v = b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var local = root.InverseTransformPoint(filter.transform.TransformPoint(v));
                    Check(Mathf.Abs(local.x) <= .901f && local.z >= -.423f && local.z <= .423f, "shipping furniture fits reserved radial envelope");
                }
            }
        }
    }
    public static int Run()
    {
        count = 0; WorkspaceClock.Now = 0; SkyAlternative.PlacedRoomRoot = null;
        var station = new GameObject("Shared station"); PlaceStation(station.transform);
        var template = TownServiceWorkspace.CounterTemplate!;
        var originals = template.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials).Distinct().ToArray();
        var originalVisibility = originals.Select(m => m.GetFloat("_TownVisibility")).ToArray();
        var workspaces = new List<TownServiceWorkspace>();
        try
        {
            Roster(1, 7, 19, 53);
            for (int slot = 0; slot < 4; slot++)
            {
                NetPlayerActors.Local = new[] { 1, 7, 19, 53 }[slot];
                var workspace = new TownServiceWorkspace(station.transform); workspaces.Add(workspace); workspace.SetVisibility(1);
                Check(workspace.RelocationRevision == 0, "initial placement does not advance relocation revision");
                // Geometry first also proves the old outward layout fails the regression check.
                if (slot > 0) CheckGeometry(workspaces);
                Check(Close(workspace.Root.position, slot == 0 ? station.transform.position : Expected(slot)), "complete roster assigns distinct ordinal including flat peers");
                Check(workspace.FurnitureRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0, "furniture clone contains no NPC");
                Check(workspace.FurnitureRoot.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "decorative furniture cannot intercept input");
                Check(workspace.Materials.All(m => !originals.Contains(m)), "each workspace owns its materials");
                Check(workspace.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - (slot == 0 ? 0 : 1)) < .0001f), "primary duplicate is hidden while extensions are visible");
                Check(workspace.FurnitureRoot.gameObject.activeSelf == (slot != 0), "primary hidden geometry costs no duplicate draw calls");
                foreach (var renderer in workspace.FurnitureRoot.GetComponentsInChildren<MeshRenderer>(true))
                { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); Check(block.isEmpty, "mesh materials are captured without unsupported property blocks"); }
            }
            for (int i = 0; i < originals.Length; i++) Check(originals[i].GetFloat("_TownVisibility") == originalVisibility[i], "shared template materials remain unchanged");
            for (int i = 0; i < workspaces.Count; i++) for (int j = i + 1; j < workspaces.Count; j++)
                Check(!workspaces[i].Materials.Any(m => workspaces[j].Materials.Contains(m)), "material fades cannot affect another visitor");
            NetPlayerActors.Local = 53; var moving = workspaces[3]; var previous = moving.Root.position;
            Roster(1, 19, 53); WorkspaceClock.Now = 1; moving.Tick(false);
            WorkspaceClock.Now = 1.15f; moving.Tick(false);
            Check(Close(moving.Root.position, previous) && moving.RelocationVisibility == 1f, "held or returning card defers relocation");
            WorkspaceClock.Now = 2; moving.Tick();
            Check(Close(moving.Root.position, previous) && moving.RelocationVisibility == 1f, "roster change begins from existing owner pose");
            Check(!moving.InputAvailable, "relocation blocks new grabs even in first fully opaque frame");
            Check(moving.RelocationRevision == 0, "fade-out does not advance relocation revision");
            WorkspaceClock.Now = 2.055f; moving.Tick();
            Check(Close(moving.Root.position, previous) && Math.Abs(moving.RelocationVisibility - .5f) < .001f, "owner dissolves without moving visible original cards");
            Check(moving.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - .5f) < .001f), "furniture publishes same intermediate relocation fade");
            WorkspaceClock.Now = 2.12f; moving.Tick();
            Check(moving.RelocationVisibility == 0f && Close(moving.Root.position, Expected(2)), "pose change has a fully invisible published frame");
            Check(moving.RelocationRevision == 1, "only invisible pose change advances relocation revision");
            WorkspaceClock.Now = 2.18f; moving.Tick(); Check(moving.RelocationVisibility > 0 && moving.RelocationVisibility < 1, "new pose fades in rather than popping");
            WorkspaceClock.Now = 2.23f; moving.Tick(); Check(moving.RelocationVisibility == 1 && Close(moving.Root.position, Expected(2)), "membership transition reaches full-opacity current ordinal");
            WorkspaceClock.Now = 4; moving.Tick(); Check(moving.RelocationRevision == 1, "ordinary fade and stable roster retain relocation revision"); Check(moving.RelocationVisibility == 1f && moving.InputAvailable, "unchanged roster cannot restart transition");
            using (var lateJoin = new TownServiceWorkspace(station.transform)) Check(Close(lateJoin.Root.position, moving.Root.position), "late join and survivor converge from native roster");
            // Ground sampling and canonical frame changes are actual production placement code.
            var room = new GameObject("sloped room"); room.transform.position = MapRoomDriver.Center; room.transform.localScale = Vector3.one * MapRoomDriver.Scale;
            var geometry = new GameObject("RoomGeo"); geometry.transform.SetParent(room.transform, false);
            var ground = new GameObject("Ground"); ground.transform.SetParent(geometry.transform, false);
            var mesh = new Mesh { vertices = new[] { new Vector3(-5, -.15f, -5), new Vector3(5, .05f, -5), new Vector3(5, .15f, 5), new Vector3(-5, -.05f, 5) }, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            try
            {
                SkyAlternative.PlacedRoomRoot = room.transform; WorkspaceClock.Now = 5; moving.Tick(); WorkspaceClock.Now = 5.12f; moving.Tick(); WorkspaceClock.Now = 5.23f; moving.Tick();
                Vector3 local = room.transform.InverseTransformPoint(moving.Root.position);
                Check(Math.Abs(local.y - (.02f * local.x + .01f * local.z)) < .0001f, "workspace rests on original sloped floor at its own target");
                Check(Math.Abs(local.y) > .005f, "slope fixture differs measurably from room-root height");
                Transform foot = moving.FurnitureRoot.Find("FootPlinth");
                Check(Math.Abs(foot.localPosition.y + foot.localScale.y * .5f - .16f) < .0001f,
                    "grounding preserves original counter support top");
                foreach (float x in new[] { -.5f, .5f }) foreach (float z in new[] { -.5f, .5f })
                {
                    Vector3 corner = foot.TransformPoint(new Vector3(x, -.5f, z));
                    float floor = TownServicePlacement.GroundHeight(room.transform, corner);
                    Check(corner.y <= floor + .001f, "workspace support bottoms cannot float over sloped ground");
                }
            }
            finally { SkyAlternative.PlacedRoomRoot = null; UnityEngine.Object.DestroyImmediate(room); UnityEngine.Object.DestroyImmediate(mesh); }
            Roster(1, 19, 53, 88); NetPlayerActors.Local = 88;
            using (var reconnect = new TownServiceWorkspace(station.transform)) Check(Close(reconnect.Root.position, Expected(3)), "new connection ID resolves next distinct seat");
            moving.SetVisibility(.4f); Check(moving.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - .4f) < .0001f), "owner service dissolve remains independent of relocation");
            moving.Dispose(); moving.Dispose(); Check(!moving.Root.gameObject.activeSelf && moving.Materials.Count == 0, "disposal hides furniture immediately and releases owned materials");
            Roster(1, 19, 53, 88, 104); NetPlayerActors.Local = 104; bool rejected = false;
            try { using var excess = new TownServiceWorkspace(station.transform); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("exceeds four users"); }
            Check(rejected, "unexpected fifth user is rejected instead of overlapping a valid seat");
            Roster(); NetPlayerActors.Local = 0;
            using (var solo = new TownServiceWorkspace(station.transform)) { solo.SetVisibility(1); Check(Close(solo.Root.position, station.transform.position), "offline keeps original front counter"); }
            return count;
        }
        finally { foreach (var workspace in workspaces) workspace.Dispose(); UnityEngine.Object.DestroyImmediate(station); }
    }
}

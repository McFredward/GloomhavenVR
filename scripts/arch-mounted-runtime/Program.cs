using System;
using UnityEngine;

public static class InteractionProgram
{
    public static int Run() => GloomhavenVR.Core.WallSegmentFade.Replay();
}

namespace GloomhavenVR.Core
{
    internal static partial class WallSegmentFade
    {
        private sealed partial class FadeDriver
        {
            private static int _checks;
            private static void Check(bool value, string message)
            {
                _checks++;
                if (!value) throw new Exception(message);
            }
            private static GameObject Child(Transform parent, string name, Vector3 position)
            {
                var child = new GameObject(name); child.transform.SetParent(parent, false);
                child.transform.localPosition = position; return child;
            }
            private static MeshRenderer MeshNode(Transform parent, string name, string meshName, Vector3 position, Vector3 size)
            {
                var go = Child(parent, name, position);
                var mesh = new Mesh { name = meshName };
                var a = -size * 0.5f; var b = size * 0.5f;
                mesh.vertices = new[] { a, b, new Vector3(a.x,b.y,a.z), new Vector3(b.x,a.y,b.z) };
                mesh.triangles = new[] { 0, 1, 2, 0, 3, 1 }; mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                return go.AddComponent<MeshRenderer>();
            }
            internal static int Replay()
            {
                _checks = 0;
                var driver = new FadeDriver(); driver.AddArch(-0.6f, 2.3f, -10.9f, -9.9f, 4.7f);
                var world = new GameObject("Scenario");
                var prefab = Child(world.transform, "TO_INT_Stone_Doorway_02_FRAME_Split_PR", new Vector3(0.85f,0,-10.4f));
                var frame = MeshNode(prefab.transform, "CR_INT_Stone_Doorway_02_FRAME_Split", "CR_INT_Stone_Doorway_02_FRAME_Split", new Vector3(0,1.7f,0), new Vector3(2.4f,3.4f,0.5f));
                var torch = MeshNode(prefab.transform, "CR_St_WallTorch_Fire", "EN_CR_WallTorch_01", new Vector3(1.15f,2.23f,0.6f), new Vector3(0.15f,0.3f,0.15f));
                var fire = Child(torch.transform, "p_fire_torch (8)", Vector3.zero);
                var particle = fire.AddComponent<ParticleSystem>();
                var main = particle.main; main.startColor = new Color(.8f,.2f,.1f,.75f); main.startSize = .37f;
                var emission = particle.emission; emission.rateOverTime = 11;
                var renderer = fire.GetComponent<ParticleSystemRenderer>();
                var light = fire.AddComponent<Light>(); light.intensity = 3.5f; light.enabled = true;
                Check(!driver.Legacy(renderer), "captured emitter lies outside the geometric arch rect");
                Check(driver.Protect(renderer), "actual split-frame sibling torch stays protected outside its arch rect");
                foreach (string name in new[] { "fx_sparks_drop", "distort", "fx_sparks (1)" })
                {
                    var fx = Child(fire.transform,name,Vector3.zero); fx.AddComponent<ParticleSystem>();
                    Check(driver.Protect(fx.GetComponent<ParticleSystemRenderer>()), "every original nested fire layer inherits the same frame attachment");
                }
                var prior = new MountedProp { Renderer = renderer };
                driver._mountedOwned.Add(renderer);
                Check(driver.ReleaseCarried(prior) && prior.Restored && !driver._mountedOwned.Contains(renderer),
                    "already hidden frame effect restitutes before sticky carry");
                Check(renderer.enabled && main.startColor.color.a == .75f && main.startSize.constant == .37f
                    && emission.rateOverTime.constant == 11 && light.enabled && light.intensity == 3.5f,
                    "ownership queries never write flame presentation or native lights");
                prefab.name = "TO_INT_Stone_Doorway_02_FRAME_Split_PR (3)(Clone)";
                Check(driver.Protect(renderer), "native clone and numeric suffixes retain exact frame provenance");
                prefab.name = "TO_INT_Stone_Doorway_02_FRAME_Split_PR(Clone) (3)";
                Check(driver.Protect(renderer), "native numeric and clone suffixes retain either authored order");
                prefab.name = "TO_INT_Stone_Doorway_02_FRAME_Split_PR foreign";
                Check(!driver.Protect(renderer), "unknown similarly named containers cannot inherit frame ownership");
                prefab.name = "TO_INT_Stone_Doorway_02_FRAME_Split_PR";
                fire.transform.SetParent(world.transform,true);
                Check(!driver.Protect(renderer), "a pooled effect loses stale frame ownership on reparenting");
                fire.transform.SetParent(torch.transform,false);
                Check(driver.Protect(renderer), "a reattached native torch reacquires live frame ownership");
                var wall = Child(world.transform,"Wall 5",Vector3.zero); wall.AddComponent<ProceduralWall>();
                fire.transform.SetParent(wall.transform,true);
                Check(!driver.Protect(renderer), "ordinary wall-mounted flame outside an arch still fades");
                fire.transform.SetParent(torch.transform,false);
                torch.GetComponent<MeshFilter>().sharedMesh.name = "ForeignTorch";
                Check(!driver.Protect(renderer), "native torch mesh identity cannot be inferred from its object name");
                torch.GetComponent<MeshFilter>().sharedMesh.name = "EN_CR_WallTorch_01";
                frame.GetComponent<MeshFilter>().sharedMesh.name = "ForeignFrame";
                Check(!driver.Protect(renderer), "native frame mesh identity cannot be inferred from its object name");
                frame.GetComponent<MeshFilter>().sharedMesh.name = "CR_INT_Stone_Doorway_02_FRAME_Split";
                frame.transform.localPosition += Vector3.right * 20;
                Check(!driver.Protect(renderer), "an unprotected frame cannot exempt its attached flame");
                frame.transform.localPosition -= Vector3.right * 20;
                torch.transform.SetParent(frame.transform,true);
                Check(driver.Protect(renderer), "original nested split-frame torch inherits its actual mesh parent");
                var actor = world.AddComponent<ActorBehaviour>();
                Check(!driver.Protect(renderer), "a gameplay actor never acquires scenery attachment ownership");
                UnityEngine.Object.DestroyImmediate(actor);
                driver.ClearArches();
                Check(!driver.Protect(renderer), "a native attachment never manufactures an absent arch protection rect");
                foreach(string name in new[] { "TO_INT_Stone_Doorway_01_FRAME_Split_PR", "TO_INT_Stone_Doorway_02_FRAME_Split_PR", "TO_EXT_Stone_Doorway_01_FRAME_Split_PR", "TO_EXT_Stone_Doorway_02_FRAME_Split_PR" })
                    Check(NativeArchEffectRoots.Contains(NativeArchName(name)), "all inspected indoor and outdoor frame families qualify");
                Check(!NativeArchEffectRoots.Contains(NativeArchName("TO_INT_Stone_Feature_Large_02_PR")), "a larger gatehouse feature is not a split-frame attachment root");
                var ordinary = new MountedProp { Renderer = renderer };
                driver._mountedOwned.Add(renderer);
                Check(!driver.ReleaseCarried(ordinary) && !ordinary.Restored && driver._mountedOwned.Contains(renderer),
                    "ordinary carried effects retain their current owner until normal restitution");
                driver.AddArch(-.6f, 2.3f, -10.9f, -9.9f, 4.7f);
                torch.transform.SetParent(prefab.transform,true);
                var quad = MeshNode(prefab.transform, "CandleFlame (2)", "OriginalFlameQuad", new Vector3(1.15f,2.23f,.6f), Vector3.one*.1f);
                Check(driver.Protect(quad), "original sibling candle flame inherits actual primitive frame provenance");
                quad.name = "ForeignFlame";
                Check(!driver.Protect(quad), "unknown sibling effects never gain candle ownership by proximity");
                foreach (var pair in NativeArchFixture.Pairs)
                {
                    prefab.name = pair[0]; frame.GetComponent<MeshFilter>().sharedMesh.name = pair[1];
                    frame.name = pair[1];
                    Check(driver.Protect(renderer), "every audited primitive doorway family protects its native mounted torch");
                    quad.name = "CandleFlame (2)";
                    Check(driver.Protect(quad), "every audited primitive doorway family protects its native sibling candle layer");
                }
                prefab.name = "TO_INT_Wood_Shack_Entrance_Thin_PR";
                Check(!driver.Protect(quad), "a broad entrance prefab never protects unrelated candles through a distant frame");
                prefab.name = "TO_INT_Wood_Shack_Doorway_01_Split_PR";
                frame.name = "TO_INT_Shack_Doorway_Split";
                frame.GetComponent<MeshFilter>().sharedMesh.name = "TO_INT_Shack_Doorway_Split";
                var composite = Child(world.transform, "Entrance", Vector3.zero);
                prefab.transform.SetParent(composite.transform,true);
                foreach (string name in NativeArchFixture.CompositeRoots)
                {
                    composite.name = name;
                    Check(driver.Protect(quad), "all four composite entrance exit families inherit through their actual nested primitive doorway");
                }
                prefab.name = "ST_Vermling_DoorFrame_PR";
                frame.name = "DLC_TH_Vermling_Door_Frame";
                frame.GetComponent<MeshFilter>().sharedMesh.name = "DLC_TH_Vermling_Door_Frame";
                var splitDoor = Child(prefab.transform,"ST_Vermling_Door_Split",Vector3.zero);
                frame.transform.SetParent(splitDoor.transform,true);
                Check(driver.Protect(renderer), "the original Vermling nested frame supplies exact attachment protection");
                // These larger original feature families retain only the narrow
                // actual holder inheritance, rather than exempting their whole tree.
                fire.transform.SetParent(torch.transform,false);
                torch.transform.localPosition = new Vector3(.6f,2.23f,.3f);
                foreach (string meshName in new[] { "EN_CR_WallTorch_01", "CR_INT_Wall_Candles_01", "CR_INT_Wall_Candles_02" })
                {
                    torch.GetComponent<MeshFilter>().sharedMesh.name = meshName;
                    Check(driver.Protect(renderer), "every original protected physical holder transfers its arch exception to attached effects");
                }
                return _checks;
            }
        }
    }
}

// Ports: original results/bootstrap/vote controllers (shared lifecycle boundary),
// hand/pixel bake adapters, network root ordering types, shared container fields.
// Actual production capture, assets/material identity, codec, clone neutralizer,
// atomic assembly and all Unity rendering execute unchanged.
#pragma warning disable CS0649,CS0414
using System;using System.Collections.Generic;using UnityEngine;using TMPro;
public class UIScenarioBattleGoalResult:MonoBehaviour { }
public class UIScenarioAchievement:MonoBehaviour { }
public partial class UINewAdventureResultsManager { public UIScenarioBattleGoalResult battleGoalResultSlot=null!;public UIScenarioAchievement partyAchievementPrefab=null!; }
namespace GloomhavenVR.Hands.Interact { internal sealed class PokeOnlyTarget:MonoBehaviour { }internal static class UguiPokeSurfaces {internal static readonly HashSet<Canvas> Admitted=new();internal static void RegisterNested(Canvas host,Canvas nested)=>Admitted.Add(nested);internal static void UnregisterNested(Canvas? host,Canvas nested)=>Admitted.Remove(nested);} }
namespace GloomhavenVR.Cards { internal static class CardFaceMipBake {internal static Sprite OriginalFor(Sprite s)=>s;internal static Sprite? PresentationFor(Sprite? s)=>s;} }
namespace GloomhavenVR.WorldUI { internal static class PanelMipBake {internal static Texture OriginalFor(Texture t)=>t;} }
namespace GloomhavenVR.Core {internal static class BundleShaders {internal static Shader? Resolve(string n,string s,string a,string b)=>Shader.Find(n);} }
// Logger output remains an explicit fixture port; the actual bounded progress
// receipt executes its DTO scan and lifecycle under an enabled normal log level.
namespace GloomhavenVR.Core {internal enum VRLogLevel { Info=3 }internal static partial class VRLog {internal static bool Wants(VRLogLevel level)=>true;} }
namespace GloomhavenVR.Net {
 // Scroll input is inactive in this original-widget/capture proof. The dedicated
 // shared-scroll runtime exercises the real native coordinator and input proxies.
 internal static class SharedWindowScrollSync {internal static void AttachResultsInputs(Transform root, Canvas? host = null) {}}
 internal sealed class RigPose {internal Vector3 Position;internal Quaternion Rotation=Quaternion.identity;}
 internal enum SharedWindowMovePhase:byte {Initial,Held,Released,Automatic}
 internal struct SharedWindowMoveEntry {internal byte Kind;internal SharedWindowMovePhase Phase;internal uint ContentKey,Opening,Claim,Sequence,ParentClaim;internal int OpeningAuthor,ParentAuthor;}
}
namespace GloomhavenVR.Net.TownServices {
 internal sealed class TownCardReturnOrigin { }
 internal sealed class TownOfferedHover { }
 internal sealed class TownRackState {internal TownRackState Copy()=>throw new InvalidOperationException("Results never populate NPC rack metadata");}
 internal sealed class TownRackStamp {internal TownRackStamp Copy()=>throw new InvalidOperationException("Results never populate NPC rack metadata");}
 internal sealed class TownCatalogBank {internal TownCatalogBank Copy()=>throw new InvalidOperationException("Results never populate NPC catalog metadata");internal TownCatalogBank Retain()=>throw new InvalidOperationException("Results never populate NPC catalog metadata");}

}

internal static class ResultsClock {internal static float Now=10;internal static int Frame=1;}

namespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {internal static bool ReleaseForComposite(UnityEngine.UI.UIWindow window){ScenarioResultsReadyPark.BeforeExternalOwner(window);return true;}} }

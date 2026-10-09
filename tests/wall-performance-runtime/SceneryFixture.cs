using System;
using UnityEngine;
namespace GloomhavenVR.Core;
internal static class ScenarioSceneryBudget
{
    // Only the actual SetHidden primitive is extracted. The whole-game scenery
    // classifier and shared collider ledger remain explicit model boundaries.
    private static Func<Renderer,bool>? _retainPerformanceWallMask=WallSegmentFade.RetainPerformanceMaskOnForeignRelease;
    private sealed class Record
    {
        internal MeshRenderer Renderer=null!;
        internal bool Owned;
        internal Collider[] BayColliders=Array.Empty<Collider>();
    }
    private static void ClaimTreeColliders(Record record){ }
    private static void ReleaseTreeColliders(Record record){ }
    private static void RefreshBayCollider(Collider collider){ }
    // @PRODUCTION_SET_HIDDEN@
    internal sealed class Fixture
    {
        private readonly Record record;
        internal Fixture(MeshRenderer renderer){record=new Record{Renderer=renderer};}
        internal bool Owned=>record.Owned;
        internal void Hide(bool hidden)=>SetHidden(record,hidden);
    }
}

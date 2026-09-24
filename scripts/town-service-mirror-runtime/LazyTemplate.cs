using System;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.WorldUI
{
    // The original game geometry provider and unrelated template families are boundaries.
    // The lazy inspection path, freeze, prune, partition and Resolve are production methods.
    internal static partial class LazyTemplateProbe
    {
        private static readonly Dictionary<string,Entry> Entries=new(StringComparer.Ordinal);
        private static readonly Dictionary<Transform,string> Roots=new();
        private static readonly List<GameObject> PhysicalTemplates=new();
        private static GameObject? _bank;
        private static bool IsBoundary(Transform node)=>Roots.ContainsKey(node);
        private static void EnsureCard(string key) { }
        private static void EnsureTooltip(string key) { }
        internal static void Open(GameObject bank)=>_bank=bank;
        internal static int Originals=>PhysicalTemplates.Count;
        internal static void Close()
        { Entries.Clear();Roots.Clear();PhysicalTemplates.Clear();_bank=null; }
    }
    internal static class TownServiceCardBody
    { internal static void RebindClone(string key,GameObject clone) { } }
    internal static class TownServiceDecor
    {
        internal static Transform? CoinTemplate=>null;
        internal static Transform? MoneyBagTemplate=>null;
        internal static int StaticPropCount(byte service)=>0;
        internal static bool TryStaticProp(byte service,int index,out Transform? source,out string key)
        {source=null;key=string.Empty;return false;}
    }
}

public static partial class MirrorProgram
{
    private static void LazyInspectionTemplate()
    {
        TownServiceMirror.Shutdown();
        GameObject bank=Go("inactive native template bank");bank.SetActive(false);
        LazyTemplateProbe.Open(bank);
        try
        {
            const string key="inspectionbody.3e0f5c29.3e0f5c29.p";
            IReadOnlyList<LazyTemplateProbe.Part> first=LazyTemplateProbe.Parts(key);
            Check(first.Count>0,"lazy inspection backing has publication partitions on its first request");
            Check(first[0].Original!=null&&first[0].Original.GetComponent<MeshRenderer>()!=null,
                "first lazy backing partition contains its real physical renderer");
            Check(!first[0].Original.gameObject.activeInHierarchy,
                "lazy native backing is frozen inside the inactive template bank");
            Check(LazyTemplateProbe.Resolve(1,1,key+"|"),
                "first backing request resolves a registered observer template");
            Transform frozen=first[0].Original;
            for(int i=0;i<20;i++)
            {
                IReadOnlyList<LazyTemplateProbe.Part> repeated=LazyTemplateProbe.Parts(key);
                Check(ReferenceEquals(first,repeated)&&repeated[0].Original==frozen&&LazyTemplateProbe.Originals==1,
                    "repeated lazy backing requests retain one frozen original and partition list");
            }
        }
        finally {LazyTemplateProbe.Close();TownServiceMirror.Shutdown();}
    }
}

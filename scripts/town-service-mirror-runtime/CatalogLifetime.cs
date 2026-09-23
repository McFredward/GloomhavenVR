using System;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static partial class MirrorProgram
{
    private static void CatalogLifetime()
    {
        TownServiceMirror.Shutdown();TownServiceSync.ResetNetwork();TownServiceSync.UseProductionPublish=true;
        var shared=Go("catalog lifetime shared frame").transform;
        var catalog=new TownServiceCatalog();
        TownServicePresentation.Active=true;TownServicePresentation.Service=1;
        TownServicePresentation.Session=900;TownServicePresentation.RelocationVisibility=1;
        TownServicePresentation.Window=Go("native catalog lifetime window").AddComponent<PublisherWindow>();
        TownServicePresentation.Catalog=catalog;
        TownServicePresentation.LocalSurfaces.Clear();TownServicePresentation.Samples.Clear();
        TownServicePresentation.CounterFurniture=null;TownServicePresentation.Tray=null;TownServicePresentation.Ritual=null;
        TownServicePresentation.WorkspaceProps=null;NativeTemplates.Tooltip=null;
        NativeTemplates.Originals.Clear();
        var ids=new Dictionary<Transform,ushort>();
        try
        {
            // Six complete pages exceed the three-page warm window. More than a thousand
            // valid turns would exhaust ushort IDs if each returning page allocated again.
            for(int i=0;i<96;i++)
            {
                var mount=Go("live catalog mount "+i).transform;mount.SetParent(shared,false);
                mount.gameObject.AddComponent<CanvasGroup>();
                var entry=new TownServiceCatalog.Entry{ItemId=1000+i,Page=i/16,CardRoot=Go("pooled native item "+i).transform,
                    BodyRoot=Go("body "+i).transform,RowSource=Go("native price "+i).transform,RowContent=Go("price copy "+i).transform};
                entry.CardRoot.SetParent(mount,false);entry.BodyRoot.SetParent(mount,false);entry.RowContent.SetParent(mount,false);
                catalog.Entries.Add(entry);TownServiceCatalog.CardMounts.Add(mount,entry);
            }
            for(int turn=0;turn<1100;turn++)
            {
                int page=turn%6;
                foreach(var entry in catalog.Entries)entry.Exposed=entry.Page==page||entry.Page==(page+1)%6||entry.Page==(page+5)%6;
                TownServiceSync.Calls.Clear();TownServiceSync.Tick(shared,shared);
                foreach(var entry in catalog.Entries)
                {
                    if(!entry.Exposed)continue;
                    foreach(Transform source in new[]{entry.MountRoot,entry.CardRoot,entry.BodyRoot!,entry.RowContent!})
                    {
                        ushort id=TownServiceSync.ModuleId(source);
                        if(ids.TryGetValue(source,out ushort before))Check(id==before,"valid page cycling preserves the original module namespace");
                        else ids.Add(source,id);
                    }
                }
                Check(TownServiceSync.ModuleCount==192,"only three physical catalog pages publish active modules");
                Check(TownServiceSync.SourceCount<=384,"retained source cache is bounded by this live catalog");
                Check(TownServiceSync.Calls.Count==192,"off-warm pages never enter the publication path");
            }
            Check(TownServiceSync.AllocatedIds==384,"complete repeated cycles allocate once per physical source");
            foreach(var entry in catalog.Entries)entry.Exposed=entry.Page<=2;
            TownServiceSync.Tick(shared,shared);
            var original=catalog.Entries[0];Transform pooled=original.CardRoot,oldMount=original.MountRoot;
            ushort oldId=TownServiceSync.ModuleId(pooled);
            var replacement=Go("replacement native inventory mount").transform;replacement.SetParent(shared,false);
            replacement.gameObject.AddComponent<CanvasGroup>();TownServiceCatalog.CardMounts.Remove(oldMount);TownServiceCatalog.CardMounts.Add(replacement,original);
            // Native pooling may hand the same exact ItemCardUI transform to a new catalog
            // before the next Sync tick. Root identity alone must not reuse stale module IDs.
            pooled.SetParent(replacement,false);original.BodyRoot!.SetParent(replacement,false);original.RowContent!.SetParent(replacement,false);
            TownServiceSync.Tick(shared,shared);
            Check(TownServiceSync.ModuleId(pooled)>oldId,"pooled native replacement never inherits the previous borrower module ID");
            Check(TownServiceSync.SourceCount==384,"replacing native inventory retires its previous source owner");
            int afterReplacement=TownServiceSync.AllocatedIds;
            original.Exposed=false;TownServiceSync.Tick(shared,shared);original.Exposed=true;TownServiceSync.Tick(shared,shared);
            Check(TownServiceSync.AllocatedIds==afterReplacement,"replacement entry remains stable across later hidden-page cycles");
            var popup=Go("unrelated transient native popup").transform;popup.SetParent(replacement,false);
            TownServicePresentation.LocalSurfaces.Add(new TownServiceSurface{Panel=new PanelFixture{Target=popup}});
            TownServiceSync.Tick(shared,shared);TownServicePresentation.LocalSurfaces.Clear();TownServiceSync.Tick(shared,shared);
            Check(TownServiceSync.SourceCount==384,"ordinary unrelated popup sources retire when unseen");
            original.Current=false;TownServiceCatalog.CardMounts.Remove(replacement);UnityEngine.Object.DestroyImmediate(replacement.gameObject);
            TownServiceSync.Tick(shared,shared);
            Check(TownServiceSync.SourceCount==380,"destroyed catalog roots retire without retaining native pool objects");
            TownServicePresentation.Session++;
            TownServiceSync.Tick(shared,shared);
            Check(TownServiceSync.SourceCount==TownServiceSync.ModuleCount,"new service session clears hidden catalog identity cache");
            TownServiceSync.Reset();Check(TownServiceSync.SourceCount==0&&TownServiceSync.ModuleCount==0,"reset clears all retained source identities");
        }
        finally
        {
            TownServiceSync.UseProductionPublish=false;TownServiceSync.ResetNetwork();TownServiceCatalog.CardMounts.Clear();
            TownServicePresentation.Catalog=null;TownServicePresentation.Window=null;TownServicePresentation.LocalSurfaces.Clear();NativeTemplates.Originals.Clear();
        }
    }
}

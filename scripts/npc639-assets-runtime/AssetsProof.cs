using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D;
using GloomhavenVR.Net.TownServices;
using Object=UnityEngine.Object;

public static class AssetsProof
{
    private sealed class Picture
    {
        public string Key,Name;public Rect Rect;public Vector2 Pivot;public Vector4 Border;
        public float Ppu;public Vector2[] Vertices,Uv;public ushort[] Triangles;
    }
    private static int assertions;
    private static void Check(bool condition,string message)
    { assertions++;if(!condition)throw new Exception(message); }
    private static AssetBundle Load(string bank)
    {
        var bundle=AssetBundle.LoadFromFile(bank);Check(bundle!=null,"original native atlas test bank loads");
        var texture=bundle.LoadAsset<Texture2D>("native/texture");
        Check(texture!=null&&texture.name=="sactx-0-4096x4096-DXT5|BC3-BattleOverlayCanvas-811e9640"
            &&texture.width==4096&&texture.height==4096&&texture.format==TextureFormat.DXT5&&texture.mipmapCount==1,
            "actual source packed texture retains complete descriptor");
        var atlas=bundle.LoadAsset<SpriteAtlas>("native/battleoverlaycanvas.spriteatlas");
        Check(atlas!=null&&atlas.spriteCount==895,"original895-member sprite atlas loads");
        return bundle;
    }
    public static IEnumerator Run(string bank,string output)
    {
        assertions=0;var owner=new TownServiceAssets();var picture=new List<Picture>();
        AssetBundle bundle=Load(bank);
        // Read every serialized original directly; its native name never contains
        // Unity's GetSprites clone suffix. The outer bundle merely exposes exact
        // original path IDs; all sprites/atlas/texture bytes are unchanged.
        foreach(string address in bundle.GetAllAssetNames().Where(x=>x.StartsWith("native/sprite/",StringComparison.Ordinal)))
        {
            Sprite sprite=bundle.LoadAsset<Sprite>(address);
            Check(sprite!=null&&sprite.texture!=null&&sprite.packed,"source native packed member is complete: "+address);
            picture.Add(new Picture{Key=owner.Key(sprite),Name=sprite.name,Rect=sprite.rect,Pivot=sprite.pivot,
                Border=sprite.border,Ppu=sprite.pixelsPerUnit,Vertices=sprite.vertices,Uv=sprite.uv,Triangles=sprite.triangles});
        }
        Check(picture.Count==895,"every original atlas member captured");
        Check(picture.Count(x=>x.Name=="Poison")==2&&picture.Count(x=>x.Name=="Disarm")==2,
            "both actual condition-icon original variants retained");
        owner.Clear();bundle.Unload(true);yield return null;
        // A real observer registry may just have scanned before a late original
        // atlas arrives. Keep its ordinary2-second census throttle active. Packed
        // preparation must independently resolve the new native dependency.
        var receiver=new TownServiceAssets();receiver.Scan();
        yield return new WaitForSecondsRealtime(0.11f);
        bundle=Load(bank);
        Check(!Resources.FindObjectsOfTypeAll<Sprite>().Any(x=>x!=null&&x.texture!=null&&x.texture.name.Contains("BattleOverlayCanvas-")),
            "receiver starts with loaded original atlas but zero materialized native sprites");
        var elapsed=Stopwatch.StartNew();
        // The old638 source has no packed preparation API. Invoke the actual
        // current production entry when present; its absence is part of that
        // exact old-source control, not a fixture replacement implementation.
        typeof(TownServiceAssets).GetMethod("PreparePackedSprites",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(receiver,null);
        foreach(Picture source in picture)
        {
            Sprite remote=receiver.Resolve<Sprite>(source.Key)??throw new Exception("missing original sprite");
            Check(remote!=null,"complete original member resolves immediately: "+source.Name);
            Check(remote.rect==source.Rect&&remote.pivot==source.Pivot&&remote.border==source.Border
                &&remote.pixelsPerUnit==source.Ppu,"logical original rect/pivot/border/PPU match: "+source.Name);
            Check(remote.vertices.SequenceEqual(source.Vertices)&&remote.uv.SequenceEqual(source.Uv)
                &&remote.triangles.SequenceEqual(source.Triangles),"exact original packed geometry and UV match: "+source.Name);
            Check(receiver.Key(remote)==source.Key,"native atlas clone uses exact source identity: "+source.Name);
        }
        elapsed.Stop();Check(elapsed.Elapsed.TotalSeconds<1,"all895 original cold native atlas members resolve in less than1 second");
        int before=Resources.FindObjectsOfTypeAll<Sprite>().Count(x=>x.name.EndsWith("(Clone)",StringComparison.Ordinal));
        for(int i=0;i<4;i++)
        {
            yield return new WaitForSecondsRealtime(0.11f);
            bool refused=false;try{receiver.Resolve<Sprite>("sprite|invented|Poison|wrong");}catch(InvalidDataException){refused=true;}
            Check(refused,"unknown same-name artwork is never substituted");
        }
        int after=Resources.FindObjectsOfTypeAll<Sprite>().Count(x=>x.name.EndsWith("(Clone)",StringComparison.Ordinal));
        Check(after==before,"repeated asset retries do not create more atlas clones");
        receiver.Clear();yield return null;
        Check(!Resources.FindObjectsOfTypeAll<Sprite>().Any(x=>x.name.EndsWith("(Clone)",StringComparison.Ordinal)
            &&x.texture!=null&&x.texture.name.Contains("BattleOverlayCanvas-")),"registry release disposes only its atlas clones");
        Check(bundle.LoadAsset<SpriteAtlas>("native/battleoverlaycanvas.spriteatlas")!=null,
            "clone disposal keeps the original native atlas alive");
        File.WriteAllText(output,assertions+" assertions; cold complete895-member resolution="+elapsed.Elapsed.TotalSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"s\n");
        bundle.Unload(true);
    }
}

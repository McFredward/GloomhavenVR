using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

internal static partial class MapButtonTooltipPresentation
{
    private static int Checks;
    private static void Check(bool value, string why) { Checks++; if (!value) throw new Exception(why); }
    private static bool Near(float a,float b) => Math.Abs(a-b)<.0001f;
    private static Node MakeNode() => new()
    {
        Kind=2, Active=true, Enabled=true, Alpha=.6f, Text="Stadt-Ereignis – verfügbar",
        Rect=new float[] { 0,0,1,1,.5f,.5f,240,80,10,20,30,1,1,1,0,0,0,1 },
        TextStyle=new float[] { 24,12,32,1,2,3,4,700,3,257,1,1,1,1,4,5,6,7 },
        Color=new(.2f,.4f,.6f,.8f), RendererColor=new(.3f,.5f,.7f,.9f),
    };
    private static Picture MakePicture(bool detail=false) => new()
    {
        Key=255, Detail=detail, Position=new(1,2,3), Scale=new(.002f,.003f,.004f),
        Rotation=new(0,0,0,1), Nodes=new[] { MakeNode() },
        Lines=detail ? new[] { "0","Not available","","" } : Array.Empty<string>(),
    };
    // Independent fixture writer: deliberately does not invoke the production serializer.
    private static byte[] Fixture(params Picture[] pictures)
    {
        using var s=new MemoryStream(); using var w=new BinaryWriter(s, new UTF8Encoding(false,true),true);
        w.Write((byte)1);w.Write((byte)255);w.Write((byte)pictures.Length);
        foreach(var p in pictures)
        {
            w.Write(p.Detail);
            foreach(float f in new[] { p.Position.x,p.Position.y,p.Position.z,p.Scale.x,p.Scale.y,p.Scale.z,p.Rotation.x,p.Rotation.y,p.Rotation.z,p.Rotation.w }) w.Write(f);
            w.Write((byte)p.Lines.Length); foreach(string line in p.Lines) FixtureText(w,line);
            w.Write((byte)p.Nodes.Length);
            foreach(var n in p.Nodes)
            {
                foreach(float f in n.Rect)w.Write(f);
                w.Write(n.Active);w.Write(n.Alpha);w.Write(n.Kind);w.Write(n.Enabled);
                foreach(float f in new[] { n.Color.r,n.Color.g,n.Color.b,n.Color.a,n.RendererColor.r,n.RendererColor.g,n.RendererColor.b,n.RendererColor.a })w.Write(f);
                if(n.Kind==2) { FixtureText(w,n.Text);foreach(float f in n.TextStyle)w.Write(f); }
            }
        }
        w.Flush();return s.ToArray();
    }
    private static void FixtureText(BinaryWriter w,string text) { byte[] b=Encoding.UTF8.GetBytes(text);w.Write((ushort)b.Length);w.Write(b); }
    private static void Main()
    {
        Parser(); Painting(); RootPose(); InheritedAlpha(); Topology();
        Console.WriteLine($"Map tooltip presentation: {Checks} production-method assertions passed.");
    }
    private static void Parser()
    {
        var title=MakePicture();var detail=MakePicture(true);
        byte[] good=Fixture(title,detail);
        Check(TryRead(good,out Picture[] parsed)&&parsed.Length==2,"valid native title and detail decode");
        Check(parsed[0].Key==255&&parsed[0].Nodes[0].Text==title.Nodes[0].Text,"owner identity and Unicode text survive parsing");
        Check(parsed[1].Detail&&parsed[1].Lines.SequenceEqual(detail.Lines),"native detail line recipe survives parsing");
        Check(parsed[0].Nodes[0].Rect.SequenceEqual(title.Nodes[0].Rect)&&parsed[0].Nodes[0].TextStyle.SequenceEqual(title.Nodes[0].TextStyle),"owner geometry and font style survive parsing");
        for(int cut=0;cut<good.Length;cut++)Check(!TryRead(good.Take(cut).ToArray(),out Picture[] _),"truncated tooltip payload rejected");
        Check(!TryRead(new byte[MaxPayload+1],out Picture[] _),"oversized tooltip payload rejected");
        Check(!TryRead(good.Concat(new byte[]{0}).ToArray(),out Picture[] _),"trailing bytes rejected");
        foreach(int offset in new[]{0,1,2})
        {
            byte[] b=(byte[])good.Clone();b[offset]=0;
            Check(!TryRead(b,out Picture[] _),"invalid version, identity or picture count rejected");
        }
        byte[] invalidBool=(byte[])good.Clone();invalidBool[3]=2;
        Check(!TryRead(invalidBool,out Picture[] _),"noncanonical bool rejected");
        byte[] count=(byte[])good.Clone();count[2]=3;
        Check(!TryRead(count,out Picture[] _),"picture count cannot exceed two");
        foreach(float n in new[]{float.NaN,float.PositiveInfinity,100001f})
        {
            var p=MakePicture();p.Scale=new(n,.003f,.004f);
            Check(!TryRead(Fixture(p),out Picture[] _),"invalid root scale rejected");
        }
        foreach(float n in new[]{0f,-.001f})
        {
            var p=MakePicture();p.Scale=new(n,.003f,.004f);
            Check(TryRead(Fixture(p),out Picture[] _),"native zero-scale endpoints and bounded overshoot remain valid");
        }
        var bad=MakePicture();bad.Rotation=new(0,0,0,2);
        Check(!TryRead(Fixture(bad),out Picture[] _),"unnormalized root rotation rejected");
        bad=MakePicture();bad.Nodes=Array.Empty<Node>();
        Check(!TryRead(Fixture(bad),out Picture[] _),"missing node hierarchy rejected");
        bad=MakePicture();bad.Nodes=Enumerable.Range(0,49).Select(_=>MakeNode()).ToArray();
        Check(!TryRead(Fixture(bad),out Picture[] _),"node budget enforced before painting");
        foreach(float alpha in new[]{-1f,1.1f,float.NaN})
        {
            bad=MakePicture();bad.Nodes[0].Alpha=alpha;
            Check(!TryRead(Fixture(bad),out Picture[] _),"invalid inherited alpha rejected");
        }
        bad=MakePicture();bad.Nodes[0].Kind=3;
        Check(!TryRead(Fixture(bad),out Picture[] _),"unsupported graphic kind rejected");
        foreach((int index,float value) in new[]{(0,1001f),(1,40f),(7,650f),(8,5000f),(9,70000f),(10,8f),(11,2f)})
        {
            bad=MakePicture();bad.Nodes[0].TextStyle[index]=value;
            Check(!TryRead(Fixture(bad),out Picture[] _),"invalid native font style rejected");
        }
        bad=MakePicture();bad.Nodes[0].Text=new string('x',4097);
        Check(!TryRead(Fixture(bad),out Picture[] _),"oversized text rejected");
        bad=MakePicture(true);bad.Lines[0]="4";
        Check(!TryRead(Fixture(title,bad),out Picture[] _),"unknown native line style rejected");
        Check(!TryRead(Fixture(detail,title),out Picture[] _),"title/detail surface order is fixed");
        byte[] utf=Fixture(title);int textOffset=46+72+1+4+1+1+32+2;utf[textOffset]=255;
        Check(!TryRead(utf,out Picture[] _),"malformed UTF8 rejected");
    }
    private static void Painting()
    {
        var rect=new RectTransform();var text=rect.gameObject.AddComponent<TMP_Text>();var group=rect.gameObject.AddComponent<CanvasGroup>();
        var previous=MakeNode();var next=MakeNode();next.Rect[8]=30;next.Rect[9]=60;next.Alpha=1;
        next.Color=new(1,1,1,1);next.RendererColor=new(1,1,1,1);
        Apply(rect,next,previous,.25f);
        Check(Near(rect.anchoredPosition3D.x,15)&&Near(rect.anchoredPosition3D.y,30),"owner node position interpolates");
        Check(Near(group.alpha,.7f),"owner inherited alpha interpolates");
        Check(Near(text.color.r,.4f)&&Near(text.canvasRenderer.Color.r,.475f),"graphic and renderer tints both interpolate");
        Check(rect.gameObject.activeSelf&&text.enabled&&!text.raycastTarget,"picture is visible and cannot intercept input");
        Check(text.text==next.Text&&text.fontSize==24&&text.fontSizeMin==12&&text.fontSizeMax==32,"native text and font sizing applied");
        Check(text.characterSpacing==1&&text.wordSpacing==2&&text.lineSpacing==3&&text.paragraphSpacing==4,"native spacing applied");
        Check((int)text.fontWeight==700&&(int)text.fontStyle==3&&(int)text.alignment==257&&(int)text.overflowMode==1,"native style and alignment applied");
        Check(text.enableAutoSizing&&text.enableWordWrapping&&text.richText&&text.margin.w==7,"native layout flags and margins applied");
        next.Text="Different cap";Apply(rect,next,previous,.1f);
        Check(rect.anchoredPosition3D.x==30&&group.alpha==1,"new text identity never morphs from unrelated geometry");
        next.Active=false;next.Enabled=false;Apply(rect,next,next,1);
        Check(!rect.gameObject.activeSelf&&!text.enabled,"owner disabled presentation is respected");
    }
    private static void RootPose()
    {
        var previous=MakePicture();var next=MakePicture();next.Position=new(3,6,9);next.Scale=new(.004f,.007f,.012f);
        var root=new RectTransform { localScale=new(100,100,100),position=new(-99,-99,-99) };
        ApplyRootPose(root,next,previous,.5f,new(10,20,30),4);
        Check(Near(root.position.x,18)&&Near(root.position.y,36)&&Near(root.position.z,54),"owner root pose uses the shared frame and map scale exactly once");
        Check(Near(root.localScale.x,.012f)&&Near(root.localScale.y,.020f)&&Near(root.localScale.z,.032f),"root scale replaces viewer scale with owner sampled scale");
        ApplyRootPose(root,next,next,1,new(0,0,0),1);
        Check(root.position.x==3&&Near(root.localScale.z,.012f),"local picture uses the identical pose path without a second map transform");
    }
    private static void InheritedAlpha()
    {
        var grandparent=new Transform();grandparent.gameObject.AddComponent<CanvasGroup>().alpha=.2f;
        var parent=new Transform { parent=grandparent };var group=parent.gameObject.AddComponent<CanvasGroup>();group.alpha=.5f;
        Check(Near(ParentAlpha(parent),.1f),"native parent alpha is multiplied across hierarchy");
        group.ignoreParentGroups=true;
        Check(Near(ParentAlpha(parent),.5f),"ignoreParentGroups stops inherited alpha traversal");
        group.enabled=false;
        Check(Near(ParentAlpha(parent),.2f),"disabled CanvasGroup must not affect inherited opacity");
    }
    private static void Topology()
    {
        Peers.Clear();Time.unscaledTime=0;
        Receive(2,Fixture(MakePicture()),0);Tick();
        Check(Peers[2].Surfaces[0].Visible&&!Peers[2].Surfaces[1].Visible,"title-only sample paints no detail");
        Time.unscaledTime=.01f;Receive(2,Fixture(MakePicture(),MakePicture(true)),.1f);Tick();
        Check(!Peers[2].Surfaces[1].Visible,"new detail must not appear before the playback cursor reaches it");
        Time.unscaledTime=.06f;Tick();
        Check(!Peers[2].Surfaces[1].Visible,"detail remains hidden during preceding title-only interval");
        Time.unscaledTime=.12f;Tick();
        Check(Peers[2].Surfaces[1].Visible,"detail appears at its owner source time");
        Receive(2,null,.2f);Tick();
        Check(Peers[2].Surfaces[1].Visible,"received clear must wait for its playback source time");
        Time.unscaledTime=.23f;Tick();
        Check(!Peers[2].Surfaces[0].Visible&&!Peers[2].Surfaces[1].Visible,"clear hides both surfaces at its playback time");

        Peers.Clear();Time.unscaledTime=0;
        Receive(2,Fixture(MakePicture()),0);Tick();
        Receive(2,null,.1f);Receive(2,Fixture(MakePicture()),.2f);
        Check(Peers[2].History.Any(s=>s.Pictures.Length==0),"close and reopen burst retains pending clear history");
        Tick();Time.unscaledTime=.11f;Tick();
        Check(!Peers[2].Surfaces[0].Visible,"close remains visible as a disappearance before queued reopening");
        Time.unscaledTime=.22f;Tick();
        Check(Peers[2].Surfaces[0].Visible,"queued reopening eventually restores the original surface");
    }
}

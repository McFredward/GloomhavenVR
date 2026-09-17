using System;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string message) { _checks++;if(!value) throw new Exception(message); }
    private static bool Near(float a,float b)=>Math.Abs(a-b)<0.001f;
    private static bool Same(Vector3 a,Vector3 b)=>Near(a.x,b.x)&&Near(a.y,b.y)&&Near(a.z,b.z);
    private static bool Same(Vector2 a,Vector2 b)=>Near(a.x,b.x)&&Near(a.y,b.y);
    private static bool Same(Quaternion a,Quaternion b)=>Near(a.x,b.x)&&Near(a.y,b.y)&&Near(a.z,b.z)&&Near(a.w,b.w);
    private static Transform Parent(float scale,float y,float yaw=0)=>new(){localScale=new(scale,scale,scale),localPosition=new(10,y,30),localRotation=Quaternion.Yaw(yaw)};
    private static RectTransform Header(Transform home)
    {
        var banner=new RectTransform {anchorMin=new(.2f,.8f),anchorMax=new(.7f,1),pivot=new(.3f,.6f),sizeDelta=new(740,112),
            anchoredPosition3D=new(24,390,2),localScale=new(.9f,.9f,.9f),localRotation=Quaternion.Yaw(.1f)};
        banner.SetParent(home,false);return banner;
    }
    private static void Pose(RectTransform banner)
    {
        Check(Same(banner.anchoredPosition3D,new Vector3(24,390,2)),"native reparent must not retain VR position");
        Check(Same(banner.localScale,new Vector3(.9f,.9f,.9f)),"native reparent must not retain VR scale");
        Check(Same(banner.localRotation,Quaternion.Yaw(.1f)),"native reparent must not retain VR rotation");
        Check(Same(banner.anchorMin,new Vector2(.2f,.8f))&&Same(banner.anchorMax,new Vector2(.7f,1))
            &&Same(banner.pivot,new Vector2(.3f,.6f))&&Same(banner.sizeDelta,new Vector2(740,112)),"root rect layout must be restored");
    }
    public static void Main()
    {
        var home=Parent(1,0);new Transform().SetParent(home,false);var banner=Header(home);int originalIndex=banner.GetSiblingIndex();
        var shop=Parent(.669f,1200,.8f);var temple=Parent(.5f,900,-.6f);var quest=Parent(1,0);
        var child=new RectTransform{sizeDelta=new(10,25)};child.SetParent(banner,false);
        var borrow=new GuildmasterBannerBorrow();
        for(int opening=0;opening<12;opening++)
        {
            Transform destination=opening%2==0?shop:temple;
            borrow.Borrow(banner,destination);Check(borrow.IsUnder(destination),"borrow attaches original banner to current destination");Pose(banner);
            child.sizeDelta=new Vector2(10,100+opening);
            banner.SetParent(quest,true);int nativeIndex=banner.GetSiblingIndex();
            Check(!Same(banner.localScale,new Vector3(.9f,.9f,.9f)),"fixture reproduces world-preserving scale contamination");
            borrow.Release();
            Check(ReferenceEquals(banner.parent,quest)&&banner.GetSiblingIndex()==nativeIndex,"release preserves parent and sibling chosen by native mode");Pose(banner);
            Check(Near(child.sizeDelta.y,100+opening),"native header child configuration must survive release");
            banner.SetParent(home,true); // Native ResetBannerParent uses worldPositionStays=true too.
        }
        borrow.Borrow(banner,shop);banner.anchorMin=new(0,0);banner.anchorMax=new(0,0);banner.pivot=new(0,0);banner.sizeDelta=new(1,1);
        borrow.Release();Check(ReferenceEquals(banner.parent,home),"still-owned banner returns to original parent");Pose(banner);
        // Sibling restore is separate from native handoff: put it in its original home slot first.
        banner.SetSiblingIndex(originalIndex);borrow.Borrow(banner,shop);borrow.Release();
        Check(banner.GetSiblingIndex()==originalIndex,"still-owned banner restores original sibling slot");
        banner.localPosition=new(9,8,7);borrow.Release();Check(Same(banner.localPosition,new Vector3(9,8,7)),"repeated release must not overwrite later native writes");
        banner=Header(home);borrow.Borrow(banner,shop);banner.SetParent(temple,true);borrow.Borrow(banner,temple);Pose(banner);
        Check(borrow.IsUnder(temple),"direct destination switch repairs prior borrow before reacquisition");borrow.Release();Pose(banner);
        var old=Header(home);var replacement=Header(home);borrow.Borrow(old,shop);old.SetParent(quest,true);
        borrow.Borrow(replacement,temple);Pose(old);Check(ReferenceEquals(old.parent,quest),"replacement never reparents previously released native instance");borrow.Release();Pose(replacement);
        var gone=Header(home);borrow.Borrow(gone,shop);gone.Destroyed=true;borrow.Release();borrow.Release();
        var lostHome=Parent(1,0);var orphan=Header(lostHome);borrow.Borrow(orphan,shop);lostHome.Destroyed=true;borrow.Release();
        Check(ReferenceEquals(orphan.parent,null),"lost native home must not leave banner under disposable host");Pose(orphan);
        var rootBanner=Header(home);rootBanner.SetParent(null,false);borrow.Borrow(rootBanner,shop);borrow.Release();
        Check(ReferenceEquals(rootBanner.parent,null),"original scene root is restored on release");Pose(rootBanner);
        var plain=new Transform{localPosition=new(1,2,3),localScale=new(.8f,.8f,.8f)};plain.SetParent(home,false);
        borrow.Borrow(plain,shop);plain.SetParent(quest,true);borrow.Release();
        Check(Same(plain.localPosition,new Vector3(1,2,3))&&Same(plain.localScale,new Vector3(.8f,.8f,.8f)),"plain transform fallback restores local pose");
        Console.WriteLine($"Banner pose: {_checks} runtime assertions passed.");
    }
}

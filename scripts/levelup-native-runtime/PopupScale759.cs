using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using UnityEngine;

public static partial class LevelUpNativeProgram
{
    private static int FramePixels759(Texture2D image)
    {int count=0;foreach(Color32 p in image.GetPixels32())if(p.r+p.g+p.b>20)count++;return count;}
    public static IEnumerator RunPopupScale759(string output)
    {
        _checks=0;WorldUIConfig.ConversionActive=WorldUIConfig.Dialogs.Value=true;
        FlatScreen.ManualScreenActive=false;VRModeStateMachine.TableInFrontOfPlayer=true;MenuWorldContext.IsReady=false;
        NativeFont();
        var harmony=_harmony=new Harmony("GloomhavenVR.PopupScale759."+typeof(LevelUpNativeProgram).Assembly.GetName().Name);
        foreach(Type patch in new[]{typeof(ConfirmationProducerScopePatch),typeof(ConfirmationRequestOriginPatch)})
            harmony.CreateClassProcessor(patch).Patch();
        try
        {
            foreach(float scale in new[]{.02f,.05f,.2f,1f,10f,47.69f,200f})
            {
                string folder=Path.Combine(output,"rig-"+scale.ToString(CultureInfo.InvariantCulture));Directory.CreateDirectory(folder);
                var owned=CroppedConfirmation754(folder,harmony,scale,true);while(owned.MoveNext())yield return owned.Current;
            }
        }
        finally {harmony.UnpatchSelf();_harmony=null;}
        yield return _checks;
    }
}

// A new popup accepts input before its content measurement has committed a
// narrower hit frame. Production then uses its host's actual rectangle. Exact
// TryIntersect/VisibleUiSurface bodies execute before real GraphicRaycaster.
namespace GloomhavenVR.WorldUI
{
    internal static partial class CanvasConversion
    {
        internal static bool TryGetHitRect(Canvas canvas,out Rect hit){hit=default;return false;}
    }
}
namespace GloomhavenVR.Hands.Interact
{
    internal static partial class UguiPokeSurfaces
    {
        internal static IReadOnlyList<Canvas>? NestedOf(Canvas canvas)
        {var result=new List<Canvas>();foreach(Canvas child in canvas.GetComponentsInChildren<Canvas>(true))if(child!=canvas)result.Add(child);return result;}
    }
}

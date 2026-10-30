using System;
using System.Linq;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class LevelUpNativeProgram
{
    private static void ConfirmationAA745(UIWindow dialog,Transform producer,string route)
    {
        var binding=PanelSupersample.ConfirmationBindings745.LastOrDefault(x=>ReferenceEquals(x.Member.Target,dialog.transform));
        Check(binding!=null,"native confirmation producer registers original member with owner AA capture: "+route);
        Check(CanvasConversion.ActivePanels.Contains(binding!.Owner)&&CanvasConversion.ActivePanels.Contains(binding.Member),
            "owner AA binding follows completed original member conversion: "+route);
        Check(Vector3.Distance(binding.Position,producer.TransformPoint(new Vector3(0,0,-32)))<.00001f
            &&Quaternion.Angle(binding.Rotation,producer.rotation)<.001f&&Vector3.Distance(binding.Scale,producer.lossyScale)<.000001f,
            "owner AA binding follows final native confirmation placement: "+route);
    }
}

using UnityEngine;
// These ports exist only to compile the unchanged helper; this fixture does not
// exercise TMP/legacy conversion or replace the actual Unity UI implementation.
namespace TMPro { public class TMP_Text:UnityEngine.UI.Graphic {public string text="";public Mesh? mesh;} public class TMP_SubMeshUI:UnityEngine.UI.Graphic {public Mesh? mesh;} }
namespace GloomhavenVR.WorldUI {internal static class CanvasConversion { internal const float FitMinAlpha=.05f; }}

// Only unrelated mip replacement and mod-bundle shader branches are excluded.
// All SpriteAtlas, Sprite, texture and mesh/UV APIs are Unity's actual runtime.
// This fixture has no baked sprites or textures; the existing baked-provenance
// focused suite independently validates those production adapters.
using UnityEngine;
namespace GloomhavenVR.Cards {internal static class CardFaceMipBake {internal static Sprite OriginalFor(Sprite s)=>s;}}
namespace GloomhavenVR.WorldUI {internal static class PanelMipBake {internal static Texture OriginalFor(Texture t)=>t;}}
namespace GloomhavenVR.Core {internal static class BundleShaders {
 internal static Shader Resolve(string name,string channel,string found,string pending)=>throw new System.InvalidOperationException("Unused mod shader adapter");
}}
// Asset discovery keeps production diagnostics; the native atlas proof does not
// depend on the unrelated BepInEx logging sink.
namespace GloomhavenVR.Core {internal static class VRLog {
 internal static bool WantsDebug => false;
 internal static void Note(string scope,string message) { }
 internal static void Debug(string scope,string message) { }
}}

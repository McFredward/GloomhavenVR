using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthCardProgram
{
    private sealed class FailedSource : IMenuDepthSource
    {
        public Graphic Graphic { get; }
        public bool IsReady => true;
        internal int Restores;
        internal FailedSource(Graphic graphic) { Graphic = graphic; }
        public void BeforeCapture(MenuDepthEye eye) => throw new InvalidOperationException("named card caller failure");
        public void AfterCapture() => Restores++;
    }

    private sealed partial class Scene
    {
        private IEnumerator ProveSafety()
        {
            var card = new Card(typeof(ItemCardUI), _root, 5); card.Rect.gameObject.SetActive(true);
            var actualMaterial = new Material(Shader.Find("UI/Default")); card.Backgrounds[0].material = actualMaterial;
            Color tint = new Color(.04f, .15f, 1, .7f); card.Backgrounds[0].color = tint;
            var spriteTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var art = new Color32[256];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                art[y * 16 + x] = new Color32(255, 255, 255, (byte)((x < 2 || x > 13) && (y < 2 || y > 13) ? 0 : 255));
            spriteTexture.SetPixels32(art); spriteTexture.Apply();
            Sprite sprite = Sprite.Create(spriteTexture, new Rect(0, 0, 16, 16), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, Vector4.one * 3);
            card.Backgrounds[0].sprite = sprite; card.Backgrounds[0].type = UnityEngine.UI.Image.Type.Sliced;
            _head.transform.SetPositionAndRotation(new Vector3(.2f, .05f, -.45f), Quaternion.LookRotation(new Vector3(-.2f, -.05f, .45f), Vector3.up));
            ++_request; while (_completed != _request) yield return null;
            var marker = card.Rect.GetComponent<MenuDepthCardPlane>();
            Vector3 left = _head.transform.position; Vector2 leftDelta = Vector2.zero;
            for (int side = 0; side < 2; side++)
            {
                _rightEye = side != 0; _head.transform.position = left + _head.transform.right * (.064f * side);
                marker.enabled = false; ++_request; while (_completed != _request) yield return null;
                Texture2D flat = Read(card, false, 200 + side); Ink flatInk = White(flat); Vector3 expected = Expected(card, flatInk);
                marker.enabled = true; ++_request; while (_completed != _request) yield return null;
                Texture2D raised = Read(card, true, 200 + side); Ink raisedInk = White(raised);
                Check(Error(raisedInk, expected, raised) < 1.8f, "each actual stereo display eye places original complete card text on the same raised physical plane");
                Vector2 delta = Vector2.Scale(raisedInk.Center - flatInk.Center, new Vector2(raised.width, raised.height));
                if (side == 0) leftDelta = delta;
                else Check((delta - leftDelta).magnitude > .65f, "both selected original display textures retain distinct stereo card depth");
                Object.DestroyImmediate(flat); Object.DestroyImmediate(raised); card.Restored();
            }
            _rightEye = false; _head.transform.position = left;
            Check(card.Backgrounds[0].material == actualMaterial && card.Backgrounds[0].color == tint
                && card.Backgrounds[0].sprite == sprite && card.Backgrounds[0].type == UnityEngine.UI.Image.Type.Sliced,
                "stereo card captures retain original sliced alpha artwork tint and material identity");

            // A native scrolling viewport clips the whole original widget, including
            // TMP. No extra card image/text is drawn outside its current native mask.
            marker.enabled = false; ++_request; while (_completed != _request) yield return null;
            Texture2D unmasked = Read(card, false, 210); Ink fullInk = White(unmasked);
            var viewport = (RectTransform)Go("native scroll viewport", _root).transform; viewport.sizeDelta = new Vector2(120, 450);
            viewport.gameObject.layer = 5; var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = false; scroll.viewport = viewport; scroll.content = card.Rect;
            var mask = viewport.gameObject.AddComponent<RectMask2D>(); card.Rect.SetParent(viewport, false);
            ++_request; while (_completed != _request) yield return null;
            Texture2D clippedFlat = Read(card, false, 211); Ink clipped = White(clippedFlat);
            Check(clipped.Count < fullInk.Count * .75f && clipped.Count > 30, "native scroll RectMask2D still clips original TMP and printed card art");
            marker.enabled = true; ++_request; while (_completed != _request) yield return null;
            Texture2D clippedRaised = Read(card, true, 211);
            Check(White(clippedRaised).Count < fullInk.Count * .8f && card.Backgrounds[0].canvasRenderer.hasRectClipping,
                "a raised original card retains its native scrolling mask without colour or TMP substitutes");
            Check(card.Backgrounds[0].material == actualMaterial && card.Backgrounds[0].color == tint,
                "mask rebuilds keep the original card material and tint"); card.Restored();
            foreach (Texture2D image in new[] { unmasked, clippedFlat, clippedRaised }) Object.DestroyImmediate(image);
            card.Rect.SetParent(_root, false); mask.enabled = false; scroll.enabled = false; card.Rect.gameObject.SetActive(false);

            // Production subview fitting retains Z while shrinking XY. Depth is
            // a fraction of the visible physical card width, not its authored width.
            var fit = (RectTransform)Go("native XY-only subview fit", _root).transform;
            fit.localScale = new Vector3(.45f, .45f, 1f);
            var fitted = new Card(typeof(ItemCardUI), fit, 6); fitted.Rect.gameObject.SetActive(true);
            ++_request; while (_completed != _request) yield return null;
            var fittedMarker = fitted.Rect.GetComponent<MenuDepthCardPlane>(); fittedMarker.enabled = false;
            ++_request; while (_completed != _request) yield return null;
            Texture2D fittedFlat = Read(fitted, false, 215); Vector3 fittedExpected = Expected(fitted, White(fittedFlat));
            fittedMarker.enabled = true; ++_request; while (_completed != _request) yield return null;
            Texture2D fittedRaised = Read(fitted, true, 215);
            Check(Error(White(fittedRaised), fittedExpected, fittedRaised) < 1.8f,
                "native XY-only fitting retains a raised depth proportional to actual physical card width");
            fitted.Restored(); Object.DestroyImmediate(fittedFlat); Object.DestroyImmediate(fittedRaised);
            fit.localScale = new Vector3(.45f, .45f, 1e-12f);
            MenuDepthSources.BeforeCapture(_root, new MenuDepthEye(false, _head.transform.position, _head, true));
            try { fitted.Restored(); }
            finally { MenuDepthSources.AfterCapture(); fit.localScale = new Vector3(.45f, .45f, 1f); }
            bool invalidEye = false;
            try { MenuDepthSources.BeforeCapture(_root, new MenuDepthEye(false, new Vector3(float.NaN, 0, 0), _head, true)); }
            catch (InvalidOperationException) { invalidEye = true; }
            finally { MenuDepthSources.AfterCapture(); }
            Check(invalidEye, "a nonfinite projection eye is rejected before borrowing native card state"); fitted.Restored();
            fitted.Rect.gameObject.SetActive(false);
            Image unrelated = Image(_root, "Full Ability Item Card names are not native identities", new Vector2(294, 450), Vector2.zero);
            ++_request; while (_completed != _request) yield return null;
            Check(unrelated.GetComponent<MenuDepthCardPlane>() == null,
                "unrelated original UI never becomes a card merely because of its name or rectangle");
            unrelated.gameObject.SetActive(false);

            // An item/monster popup can itself own another typed modifier/card.
            // All exact child silhouette graphics remain available to depth passes;
            // its geometry inherits one parent loan, never a second added loan.
            var parent = new Card(typeof(ItemCardUI), _root, 5); parent.Title.gameObject.SetActive(false);
            var child = new Card(typeof(UIPerkAttackModifier), parent.Rect, 5); child.Rect.gameObject.SetActive(true);
            parent.Rect.gameObject.SetActive(true);
            bool childMoved = false, parentMoved = false;
            Camera.CameraCallback inspect = camera =>
            {
                if (camera != (_path == 0 ? _head : _ui)) return;
                childMoved |= child.Rect.localPosition != child.Position || child.Rect.localScale != child.Scale;
                parentMoved |= parent.Rect.localPosition != parent.Position;
            };
            Camera.onPreRender += inspect;
            ++_request; while (_completed != _request) yield return null;
            Camera.onPreRender -= inspect;
            Check(parentMoved && !childMoved, "nested native card and modifier content inherits exactly one raised parent plane");
            var parentMarker = parent.Rect.GetComponent<MenuDepthCardPlane>(); var childMarker = child.Rect.GetComponent<MenuDepthCardPlane>();
            Check(childMarker != null && childMarker.IsReady && childMarker.DepthGraphics.Count == 1,
                "nested native modifier art still exposes its exact current native depth silhouette");
            Texture2D nestedRaised = Read(child, true, 220);
            parentMarker.enabled = childMarker!.enabled = false; ++_request; while (_completed != _request) yield return null;
            Texture2D nestedFlat = Read(child, false, 220);
            Check(Error(White(nestedRaised), Expected(parent, White(nestedFlat)), nestedRaised) < 1.8f,
                "nested item modifiers render original text on the single parent card depth");
            Object.DestroyImmediate(nestedRaised); Object.DestroyImmediate(nestedFlat); parent.Restored(); child.Restored();
            parent.Rect.gameObject.SetActive(false);

            card.Rect.gameObject.SetActive(true); marker.enabled = false;
            ++_request; while (_completed != _request) yield return null;
            Texture2D native = Read(card, false, 230);
            if (_path == 0)
            {
                // Use actual Canvas phases and automatic frame progression. This
                // does not invoke the private early-prime callback by reflection.
                bool borrowed = false;
                Canvas.WillRenderCanvases? skippedHead = null;
                skippedHead = () =>
                {
                    Canvas.willRenderCanvases -= skippedHead;
                    borrowed = card.Rect.localPosition != card.Position;
                    VRRigDriver.HeadCamera = null; _head.enabled = false;
                };
                marker.enabled = true; Canvas.willRenderCanvases += skippedHead;
                int frame = Time.frameCount; while (Time.frameCount < frame + 2) yield return null;
                Canvas.willRenderCanvases -= skippedHead;
                Check(borrowed, "orphan proof observes an actual early native card transform loan before skipping the head draw");
                card.Restored();
                VRRigDriver.HeadCamera = _head; _head.enabled = true; marker.enabled = false;
                ++_request; while (_completed != _request) yield return null;
                Texture2D restored = Read(card, false, 231);
                Check(Same(native, restored), "orphaned prime and missing head restore exact original native card pixels before recovery input");
                Object.DestroyImmediate(restored);
            }
            var failure = new FailedSource(card.Backgrounds[0]); MenuDepthSources.Register(failure); marker.enabled = true;
            try
            {
                ++_request; while (_completed != _request) yield return null;
                card.Restored(); Check(failure.Restores > 0, "an actual source preparation exception restores the original card lease");
            }
            finally { MenuDepthSources.Unregister(failure); marker.enabled = false; }
            ++_request; while (_completed != _request) yield return null;
            Texture2D recovered = Read(card, false, 232);
            Check(Same(native, recovered), "an original capture exception restores exact native card pixels on the next normal draw");
            Object.DestroyImmediate(native); Object.DestroyImmediate(recovered);
            card.Rect.gameObject.SetActive(false);
            Object.DestroyImmediate(actualMaterial); Object.DestroyImmediate(sprite); Object.DestroyImmediate(spriteTexture);
        }
        private static bool Same(Texture2D a, Texture2D b)
        {
            Color32[] x = a.GetPixels32(), y = b.GetPixels32();
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (x[i].r != y[i].r || x[i].g != y[i].g || x[i].b != y[i].b || x[i].a != y[i].a) return false;
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class PalmRenderProgram
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    private static string Argument(string key) { var args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    private static GameObject Go(string name, Transform parent = null) { var go = new GameObject(name, typeof(RectTransform)); if (parent != null) go.transform.SetParent(parent, false); return go; }
    private static void Save(Camera camera, string name)
    {
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(Argument("-renderOutput"), name + ".png"), image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
    }
    private static void Label(string text, Transform parent, Vector3 position, float size)
    {
        var label = Go(text, parent).AddComponent<TextMeshPro>(); label.text = text;
        label.fontSize = size; label.color = Color.white; label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(.5f, .09f); label.transform.localPosition = position;
        label.transform.localRotation = Quaternion.Euler(0, 180, 0);
    }
    private static Mesh Book()
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        foreach (string line in File.ReadLines(Argument("-nativeBookObj")))
        {
            string[] fields = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (fields[0] == "v")
                vertices.Add(Quaternion.Euler(-90, 0, 0) * new Vector3(-float.Parse(fields[1], CultureInfo.InvariantCulture), float.Parse(fields[2], CultureInfo.InvariantCulture), float.Parse(fields[3], CultureInfo.InvariantCulture)));
            if (fields[0] == "f")
                for (int i = 2; i + 1 < fields.Length; i++)
                { triangles.Add(int.Parse(fields[1].Split('/')[0]) - 1); triangles.Add(int.Parse(fields[i + 1].Split('/')[0]) - 1); triangles.Add(int.Parse(fields[i].Split('/')[0]) - 1); }
        }
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
        var bounds = mesh.bounds; float fit = .32f / Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        for (int i = 0; i < vertices.Count; i++) vertices[i] = (vertices[i] - bounds.center) * fit + Vector3.up * (bounds.size.y * fit * .5f);
        mesh.SetVertices(vertices); mesh.RecalculateBounds(); mesh.RecalculateNormals(); return mesh;
    }
    public static void Run()
    {
        var settings = Resources.Load<TMP_Settings>("TMP Settings");
        Check(settings != null, "Imported TMP essentials supply actual typography defaults");
        typeof(TMP_Settings).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, settings);
        var font = TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("FixtureFont"));
        Check(font != null, "Imported local fixture font builds a real TMP glyph atlas");
        typeof(TMP_Settings).GetField("m_defaultFontAsset", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, font);
        var avatar = new Texture2D(8, 8); var pixels = new Color[64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = i % 2 == 0 ? new Color(.15f, .65f, 1f) : new Color(.95f, .7f, .25f);
        avatar.SetPixels(pixels); avatar.Apply(); NetPlayerActors.Avatar = Sprite.Create(avatar, new Rect(0, 0, 8, 8), Vector2.one * .5f);
        var camera = Go("Head").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.08f, .1f, .12f); camera.transform.position = new Vector3(0f, 1.37f, 1.7f);
        camera.transform.LookAt(new Vector3(0, 1.37f, 0)); camera.fieldOfView = 39f;
        GloomhavenVR.Rig.VRRigDriver.HeadCamera = camera;
        var station = Go("MageResident").transform;
        var actor = Go("Actor", station).transform; var head = Go("Head", actor).transform;
        head.localPosition = new Vector3(0, 1.63f, 0);
        var palm = Go("ActivityOfferingPalm", station).transform; palm.localPosition = new Vector3(-.18f, 1.17f, .23f);
        var seat = Go("OfferingSeat", station).transform; seat.localPosition = palm.localPosition + Vector3.up * .17f;
        var worktop = GameObject.CreatePrimitive(PrimitiveType.Cube); worktop.name = "Geometry boundary worktop";
        worktop.transform.SetParent(station, false); worktop.transform.localPosition = new Vector3(0, .94f, 0);
        worktop.transform.localScale = new Vector3(.8f, .03f, .55f);
        worktop.GetComponent<Renderer>().sharedMaterial = BoardVisual.Unlit(new Color(.21f, .15f, .11f));
        var book = Go("Original CR_ST_Shelf_Book_07", station); book.transform.localPosition = new Vector3(-.18f, .957f, .12f);
        book.AddComponent<MeshFilter>().sharedMesh = Book(); book.AddComponent<MeshRenderer>().sharedMaterial = BoardVisual.Unlit(new Color(.77f, .69f, .49f));
        float bookTop = book.transform.localPosition.y + book.GetComponent<MeshFilter>().sharedMesh.bounds.max.y;
        Label("Original game book geometry", station, new Vector3(.04f, .90f, .32f), .026f);
        var native = Go("NativeRuneDecision"); native.AddComponent<UIWindow>(); native.AddComponent<CanvasGroup>();
        var box = native.AddComponent<UIEnhancementConfirmationBox>();
        Component Text(string name) { var text = Go(name, native.transform).AddComponent<TextMeshProUGUI>(); text.text = name; text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.fontSize = 42; text.enableAutoSizing = true; text.fontSizeMax = 42; text.fontSizeMin = 12; return text; }
        Button Button(string name) { var go = Go(name, native.transform); var image = go.AddComponent<Image>(); image.color = new Color(.15f, .35f, .45f); var button = go.AddComponent<Button>(); button.targetGraphic = image; var label = Go("Label", go.transform).AddComponent<TextMeshProUGUI>(); label.text = name; label.alignment = TextAlignmentOptions.Center; label.fontSize = 40; label.color = Color.white; label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.sizeDelta = Vector2.zero; return button; }
        box.titleText = Text("Enhancement decision"); box.informationText = Text("Original native controls - fixture content");
        box.confirmButton = Button("Confirm"); box.cancelButton = Button("Cancel");
        box.enhancementName = Text("Enhancement name"); box.enhancementIcon = Go("Native Icon", native.transform).AddComponent<Image>(); box._onConfirmCallback = () => { };
        foreach (Component component in new[] { box.titleText, box.informationText, box.confirmButton, box.cancelButton, box.enhancementIcon, box.enhancementName })
            ((RectTransform)component.transform).sizeDelta = new Vector2(400, 80);
        float minimumControls = float.MaxValue;
        using (var badge = new TownServiceOccupationBadge(3, station))
        {
            TownServiceGrantSync.Owner = 0; badge.Tick(true);
            Check(station.Find("OwnerTag[22]") == null, "Free mage has no owner tag");
            TownServiceGrantSync.Owner = 22; badge.Tick(true);
            Transform tag = station.Find("OwnerTag[22]");
            Check(tag != null && tag.gameObject.activeSelf, "Physical mage lease creates a required identity even with cosmetic name tags off");
            Check(tag.Find("Avatar").GetComponent<Renderer>().sharedMaterial.mainTexture == avatar && tag.Find("Name").GetComponent<TextMeshPro>().text == "Guest 22", "Production OwnerTag uses avatar and name from the same native player identity");
            Check(Vector3.Distance(tag.position, head.position + Vector3.up * .24f) < 1e-6f, "Owner identity tracks actual actor head plus authored clearance");
            foreach (float yaw in new[] { -80f, 0f, 80f })
            {
                TownServicePalmConfirmation.Clear();
                box.GetComponent<UIWindow>().IsOpen = box.GetComponent<UIWindow>().IsVisible = true;
                seat.rotation = Quaternion.Euler(0, 180 + yaw, 0);
                TownServicePalmConfirmation.Begin(box, seat); TownServicePalmConfirmation.Tick();
                var entry = new List<TownServicePalmConfirmation.Entry>(TownServicePalmConfirmation.Active)[0];
                foreach (var surface in entry.Surfaces)
                {
                    var corners = new Vector3[4]; surface.Panel.HostRect.GetWorldCorners(corners);
                    foreach (Vector3 point in corners)
                    { float height = station.InverseTransformPoint(point).y; minimumControls = Mathf.Min(minimumControls, height); Check(height > bookTop + .02f, "Rendered native decision clears actual book envelope at every yaw"); }
                }
                Save(camera, "mage-yaw-" + yaw.ToString(CultureInfo.InvariantCulture));
            }
            badge.Tick(false); Check(!tag.gameObject.activeSelf, "Retired physical occupancy is hidden on the same tick before Unity deferred destruction");
            Save(camera, "mage-release");
        }
        using (var temple = new TownServiceOccupationBadge(2, station))
        { TownServiceGrantSync.Owner = 33; temple.Tick(true); Check(station.Find("OwnerTag[33]") == null, "Temple native commit mutex never creates an occupancy tag"); }
        using (var merchant = new TownServiceOccupationBadge(1, station))
        { TownServiceGrantSync.Owner = 44; merchant.Tick(true); Check(station.Find("OwnerTag[44]") != null, "Merchant physical lease uses the same required native owner identity"); merchant.Tick(false); Check(!station.Find("OwnerTag[44]").gameObject.activeSelf, "Merchant release hides occupation immediately"); }
        File.WriteAllText(Path.Combine(Argument("-renderOutput"), "result.json"), "{\"assertions\":" + _checks + ",\"book_top_metres\":" + bookTop.ToString(CultureInfo.InvariantCulture) + ",\"minimum_control_clearance_metres\":" + (minimumControls - bookTop).ToString(CultureInfo.InvariantCulture) + ",\"boundaries\":\"native callback/lease/player avatar fixtures; production geometry/OwnerTag/badge/TMP and original book mesh\"}\n");
    }
}

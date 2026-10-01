using System;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The unmodified production tween operates on real Unity transforms, canvas groups
/// and graphics. Packet application is represented only by writing each exact owner endpoint.</summary>
public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .00001f;

    public static int Run()
    {
        _checks = 0;
        Transform parent = new GameObject("shared map frame").transform;
        Transform host = new GameObject("observer purse module").transform;
        host.SetParent(parent, false);
        Transform body = new GameObject("original purse mesh").transform;
        body.SetParent(host, false);
        var motion = new TownServiceMotion(host, new[] { body }, "ritual.purse.held|");
        Vector3 endpoint = new Vector3(.24f, .18f, -.08f);
        Quaternion rotation = Quaternion.Euler(35f, 80f, -25f);
        motion.BeforeApply(0f);
        body.localPosition = endpoint; body.localRotation = rotation;
        motion.AfterApply(0f, .2f);
        Check(Near(body.localPosition, Vector3.zero), "held purse starts at its displayed pose, without a packet jump");
        motion.Tick(.12f);
        Vector3 middle = body.localPosition;
        Check(middle.x > .08f && middle.x < .2f,
            "held purse continues moving between slower owner samples instead of stopping at 100 ms");
        motion.Tick(.18f);
        Check(body.localPosition.x > middle.x && body.localPosition.x < endpoint.x,
            "held purse still progresses late in the owner sample interval");
        Check(Quaternion.Angle(body.localRotation, rotation) > 1f,
            "held purse rotation uses the same continuous owner sample clock");
        motion.Tick(.23f);
        Check(Near(body.localPosition, endpoint) && Quaternion.Angle(body.localRotation, rotation) < .01f,
            "held purse reaches the exact authored pose within the bounded interval");

        // Arriving samples keep the currently displayed intermediate pose, rather than restoring
        // a prior target on-screen. Unchanged properties still restore that full target internally.
        motion.BeforeApply(.23f);
        body.localPosition = new Vector3(.48f, -.12f, .15f);
        motion.AfterApply(.23f, .2f);
        motion.Tick(.33f);
        Vector3 before = body.localPosition;
        motion.BeforeApply(.33f);
        Check(Near(body.localPosition, new Vector3(.48f, -.12f, .15f)),
            "binding's unchanged properties start from the complete previous owner target");
        body.localPosition = new Vector3(.7f, .1f, -.05f);
        motion.AfterApply(.33f, .1f);
        Check(Near(body.localPosition, before), "new purse sample preserves its visible intermediate pose");
        motion.Tick(.38f);
        Check(body.localPosition.x > before.x && body.localPosition.x < .7f,
            "successive purse samples interpolate from the shown pose");
        motion.Reset();
        Check(Near(body.localPosition, new Vector3(.7f, .1f, -.05f)),
            "explicit purse retirement releases the exact complete owner target");

        // A moved parent/new visibility is an explicit lifetime boundary; blending unrelated
        // local frames would send the purse across the room or replay an old hidden hand pose.
        motion.BeforeApply(.4f);
        Transform other = new GameObject("other shared mount").transform;
        body.SetParent(other, false); body.localPosition = new Vector3(2f, 1f, 0f);
        motion.AfterApply(.4f, .2f); motion.Tick(.42f);
        Check(Near(body.localPosition, new Vector3(2f, 1f, 0f)),
            "purse reparent starts at its actual new owner pose");
        body.gameObject.SetActive(false); motion.BeforeApply(.5f);
        body.gameObject.SetActive(true); body.localPosition = new Vector3(-1f, .2f, .3f);
        motion.AfterApply(.5f, .2f); motion.Tick(.52f);
        Check(Near(body.localPosition, new Vector3(-1f, .2f, .3f)),
            "newly visible purse never interpolates from an inactive pose");

        // Confirmation facing already shares this continuous clock. Ordinary cabinet controls
        // keep their existing rapid mechanical response, and a hidden purse is not a hand pose.
        Response("item.confirm.part.2|", continuous: true);
        Response("enhance.confirm.part.0|", continuous: true);
        Response("item.41|", continuous: true);
        Response("item.41|QmFja2dyb3VuZA==:0", continuous: true);
        Response("inspectionbody.3dcccccd.3e99999a.p|", continuous: true);
        Response("merchant.heldstock|", continuous: true);
        Response("merchant.heldstock.invalid|", continuous: false);
        Response("merchant.crank|", continuous: false);
        Response("item.confirm|", continuous: false);
        Response("item.invalid|", continuous: false);
        Response("ritual.purse|", continuous: false);
        Response("ritual.purse.held.invalid|", continuous: false);

        Transform panel = new GameObject("owner original confirmation", typeof(RectTransform), typeof(Image), typeof(CanvasGroup)).transform;
        var image = panel.GetComponent<Image>(); var group = panel.GetComponent<CanvasGroup>();
        var appearance = new TownServiceMotion(panel, Array.Empty<Transform>(), "item.confirm.part.2|");
        appearance.BeforeApply(0f); image.color = Color.red; group.alpha = .2f;
        ((RectTransform)panel).sizeDelta = new Vector2(150f, 90f); appearance.AfterApply(0f, .2f);
        appearance.Tick(.11f);
        Check(image.color.g > .2f && image.color.g < .8f && group.alpha > .3f && group.alpha < .8f,
            "continuous facing retains intermediate original colors and canvas alpha");
        appearance.Tick(.23f);
        Check(image.color == Color.red && Math.Abs(group.alpha - .2f) < .00001f
            && ((RectTransform)panel).sizeDelta == new Vector2(150f, 90f),
            "continuous facing retains exact original visual endpoints");
        UnityEngine.Object.DestroyImmediate(parent.gameObject);
        UnityEngine.Object.DestroyImmediate(other.gameObject);
        UnityEngine.Object.DestroyImmediate(panel.gameObject);
        return _checks;
    }

    private static void Response(string address, bool continuous)
    {
        Transform control = new GameObject("response probe").transform;
        var motion = new TownServiceMotion(control, Array.Empty<Transform>(), address);
        motion.BeforeApply(0f); control.localPosition = Vector3.right;
        motion.AfterApply(0f, .2f); motion.Tick(.12f);
        Check(continuous ? control.localPosition.x > .4f && control.localPosition.x < .8f
            : Near(control.localPosition, Vector3.right),
            continuous ? "original palm confirmation keeps continuous facing" : "non-held discrete controls retain their rapid response");
        motion.Tick(.26f);
        Check(Near(control.localPosition, Vector3.right), "all motion classes reach the exact authored endpoint");
        UnityEngine.Object.DestroyImmediate(control.gameObject);
    }
}

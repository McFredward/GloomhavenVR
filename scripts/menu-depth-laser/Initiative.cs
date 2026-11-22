using System;
using System.Collections;
using System.IO;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GloomhavenVR.Hands.Interact;

// The native scenario's actor bootstrap and the settled pin state are inputs.
// ExtendedButton and all mod ray and Unity pointer logic run.
public sealed class FixtureInitiativeActor : InitiativeTrackActorBehaviour
{
    public override CActor.EType ActorType() => CActor.EType.Player;
    public override void OnAvatarHighlight(bool active) { }
    public override void OnDisable() { }
}

public static partial class MenuDepthLaserProgram
{
    private static (ExtendedButton button, Image header) Portrait(Scene scene, string name,
        Vector3 position, UnityAction onClick)
    {
        var actor = Rect(name, scene.Root, new Vector2(70, 60));
        actor.localPosition = position;
        var owner = actor.gameObject.AddComponent<FixtureInitiativeActor>();
        var content = Rect("ActorContent", actor, new Vector2(90, 64));
        Image header = Picture("Header", content, new Vector2(90, 64), Color.green);
        var button = content.gameObject.AddComponent<ExtendedButton>();
        button.targetGraphic = header;
        button.animateScaling = false;
        button.highlightScaleFactor = 1f; // InitiativePortraitPin neutralizes the authored response
        button.onMouseEnter = new UnityEvent();
        button.onMouseExit = new UnityEvent();
        button.onSelected = new UnityEvent();
        button.onDeselected = new UnityEvent();
        button.onClick.AddListener(onClick);
        owner.avatarButton = button;
        return (button, header);
    }

    public static IEnumerator RunInitiativePortrait(string output, Action<int> count)
    {
        _checks = 0;
        var nativePorts = NativePorts();
        try
        {
            using var scene = new Scene();
            int left = 0, right = 0, popup = 0;
            var (button, picture) = Portrait(scene, "Original Player", new Vector3(-50, 0, -50), () => left++);
            var (neighbour, other) = Portrait(scene, "Original Enemy", new Vector3(70, 0, -30), () => right++);
            Image popupPicture = Picture("Non-portrait native popup", scene.Root, new Vector2(100, 60), Color.blue);
            popupPicture.rectTransform.localPosition = new Vector3(0, -90, 0);
            popupPicture.gameObject.AddComponent<Button>().onClick.AddListener(() => popup++);
            DepthPortraitPicks.Register(scene.Canvas, new FixtureInitiativePicker(scene.Root));
            try
            {
                Canvas.ForceUpdateCanvases();
                yield return null;
                Vector3 center = picture.rectTransform.TransformPoint(Vector3.zero);
                scene.Aim(center);
                float distance = Vector3.Distance(scene.Hand.Ray.Current.Origin, center);
                SetBlocker(scene, distance + .04f, board: true);
                scene.Tick();
                Check(scene.Driver.Hovered == button.gameObject
                        && Vector3.Distance(scene.Hand.Ray.WorldUiHit, center) < .0001f
                        && Mathf.Abs(scene.Driver.HitDistance - distance) < .0001f,
                    "physical portrait in front of the board owns the press candidate and beam endpoint");
                TriggerDown(scene);
                Check(scene.Driver.IsPressing, "physical portrait in front of the board owns the press");
                TriggerUp(scene);
                Check(left == 1 && right == 0, "native original avatar click executes once through the depth candidate");

                // The header extends past the original 70 px layout cell.
                // Check both pointer edges with the settled native pin state.
                foreach(float x in new[] { -44f, 44f })
                {
                    scene.Aim(picture.rectTransform.TransformPoint(new Vector3(x, 0f, 0f)));
                    TriggerDown(scene);
                    Check(scene.Driver.IsPressing && button.transform.localScale == Vector3.one,
                        "the visible edge of a pinned native portrait receives pointer-down");
                    TriggerUp(scene);
                }
                Check(left == 3 && right == 0, "both edges click the exact original pinned avatar");

                // If the portrait sits behind its host, the board can occlude
                // it even after the aim ray crossed the earlier flat plane.
                button.transform.parent.localPosition = new Vector3(-50, 0f, 50f);
                Canvas.ForceUpdateCanvases();
                Vector3 behind = picture.rectTransform.position;
                scene.Aim(behind);
                SetBlocker(scene, Vector3.Distance(scene.Hand.Ray.Current.Origin, behind) - .04f, board: true);
                TriggerDown(scene);
                Check(!scene.Driver.IsPressing, "a flat foreground plane cannot bypass a nearer board");
                TriggerUp(scene);
                button.transform.parent.localPosition = new Vector3(-50, 0f, -50f);
                Canvas.ForceUpdateCanvases();

                scene.Aim(center);
                TriggerDown(scene);
                SetBlocker(scene, distance - .03f, board: true);
                TriggerUp(scene);
                Check(left == 3, "a board crossing the ray after press suppresses the click");
                SetBlocker(scene, distance - .03f, board: false);
                TriggerDown(scene);
                Check(!scene.Driver.IsPressing, "a genuinely nearer fan blocks pointer-down");
                TriggerUp(scene);
                scene.Hand.Ray.SolidOccluderDistance = float.PositiveInfinity;
                AimWithPhysics(scene, center, distance - .03f);
                TriggerDown(scene);
                Check(!scene.Driver.IsPressing, "a genuinely nearer physics hit blocks pointer-down");
                TriggerUp(scene);

                scene.Aim(center);
                TriggerDown(scene);
                scene.Aim(other.rectTransform.TransformPoint(Vector3.zero));
                TriggerUp(scene);
                Check(left == 3 && right == 0, "release on a neighbour cannot click either portrait");

                scene.Aim(center);
                TriggerDown(scene);
                Check(scene.Driver.IsPressing, "the original native portrait owns a live press");
                DepthPortraitPicks.Unregister(scene.Canvas);
                TriggerUp(scene);
                Check(left == 3, "a retired portrait canvas cannot deliver the old click");
                DepthPortraitPicks.Register(scene.Canvas, new FixtureInitiativePicker(scene.Root));

                scene.Aim(center);
                TriggerDown(scene);
                var raycaster = scene.Canvas.GetComponent<GraphicRaycaster>();
                raycaster.enabled = false;
                TriggerUp(scene);
                Check(left == 3, "native modal raycaster lock still revokes a held portrait");
                raycaster.enabled = true;
                scene.Hand.Grabber.TriggerGrabOffered = true;
                TriggerDown(scene);
                Check(!scene.Driver.IsPressing, "a real near-hand offer still owns the trigger");
                TriggerUp(scene);
                scene.Hand.Grabber.TriggerGrabOffered = false;

                // The angled aim ray passes just beyond the original Header at
                // its plane. The eye ray through the host plane lands inside it.
                scene.Driver.Cancel();
                Canvas.ForceUpdateCanvases();
                Vector3 flatTrap = picture.rectTransform.TransformPoint(new Vector3(45.2f, 0f, 0f));
                scene.Aim(flatTrap);
                Check(!DepthPortraitPicks.TryRaycast(scene.Canvas, scene.Hand.Ray.Current.Origin,
                          scene.Hand.Ray.Current.Direction, out _),
                    "the true hand ray misses the portrait");
                Vector3 eye = scene.Hand.Ray.Current.Origin;
                Vector3 aim = scene.Hand.Ray.Current.Direction;
                Vector3 hostPoint = eye + aim * (-eye.z / aim.z);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(scene.Head, hostPoint);
                Check(RectTransformUtility.RectangleContainsScreenPoint(picture.rectTransform, screen, scene.Head),
                    "the flattened host-plane projection would aim at the native avatar");
                TriggerDown(scene);
                TriggerUp(scene);
                Check(left == 3 && right == 0, "a flat fallback cannot activate an avatar missed by the actual beam");

                scene.Aim(popupPicture.rectTransform.position);
                TriggerDown(scene);
                TriggerUp(scene);
                Check(popup == 1, "non-portrait widgets retain ordinary native raycast and click");

                // Host bounds do not include this protruding Header. This also
                // exercises the real graphic leaf whose button root is not a Graphic.
                ((RectTransform)neighbour.transform.parent).localPosition = new Vector3(560, 0, -30);
                Canvas.ForceUpdateCanvases();
                scene.Aim(other.rectTransform.position);
                Check(!scene.Root.rect.Contains(scene.Root.InverseTransformPoint(other.rectTransform.position)),
                    "overflow witness is outside the fitted host");
                TriggerDown(scene);
                TriggerUp(scene);
                Check(right == 1, "native portrait outside the host rect clicks at its actual geometry");

                scene.Draw(Path.Combine(output, "initiative-portrait-hit.png"));
                count(_checks);
            }
            finally
            {
                DepthPortraitPicks.Unregister(scene.Canvas);
            }
        }
        finally
        {
            nativePorts.UnpatchAll(nativePorts.Id);
        }
    }

    private static void SetBlocker(Scene scene, float distance, bool board)
    {
        scene.Hand.Ray.SolidOccluderDistance = distance;
        scene.Hand.Ray.SolidOccluderIsBoard = board;
    }

    private static void AimWithPhysics(Scene scene, Vector3 target, float physicsDistance)
    {
        scene.Aim(target);
        PickPose ray = scene.Hand.Ray.Current;
        scene.Hand.Ray.Current = new PickPose(ray.Origin, ray.Direction, true, physicsDistance);
    }

    private static void TriggerDown(Scene scene)
    {
        scene.Hand.TriggerPressed = scene.Hand.TriggerDown = true;
        scene.Tick();
        scene.Hand.TriggerDown = false;
    }

    private static void TriggerUp(Scene scene)
    {
        scene.Hand.TriggerPressed = false;
        scene.Tick();
    }
}

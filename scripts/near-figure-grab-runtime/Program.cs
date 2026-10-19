using System;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Require(bool condition, string reason)
    { _assertions++; if (!condition) throw new InvalidOperationException("ASSERT: " + reason); }
    private static VRHand NewHand(HandSide side = HandSide.Left)
    {
        VRInteractables.Clear();
        FigureGrabConfig.GrabFigures.Value = true;
        VRHands.Left = VRHands.Right = VRHands.Primary = null;
        Time.frameCount = 20; Time.unscaledTime = 1;
        return new(side);
    }
    private static Collider Add(Target target, float scale = 1, float height = .08f)
    {
        var shape = new Collider { Min = new(-.01f * scale, 0, -.01f * scale), Max = new(.01f * scale, height * scale, .01f * scale) };
        if (target is Figure figure) figure.SetPickVolume(shape);
        VRInteractables.RegisterGrabbable(target, shape);
        return shape;
    }
    private static void Advance(VRHand hand)
    {
        Time.frameCount++; Time.unscaledTime += 1f / 90f;
        hand.TriggerDown = hand.GripDown = false;
    }
    private static void Press(VRHand hand) { hand.TriggerDown = hand.TriggerPressed = true; }
    private static void CheckOffer(VRHand hand, Figure figure, string context)
    {
        hand.Grabber.Tick();
        Require(ReferenceEquals(hand.Grabber.Highlighted, figure) && figure.Glow, context + ": genuine figure glow");
        Require(hand.Grabber.FigureGrabOffered, context + ": live figure offer");
    }

    private static void WorldHoverDoesNotVeto()
    {
        foreach (HandSide side in Enum.GetValues<HandSide>())
        foreach (int age in new[] { 0, 1, 2 })
        foreach (bool producerFirst in new[] { false, true })
        {
            VRHand hand = NewHand(side); Figure figure = new(); Add(figure);
            hand.Rig.PalmCenter.position = new(.026f, .04f, 0);
            CheckOffer(hand, figure, "16 mm contact");
            Advance(hand);
            hand.Ray.SetWorldUiHit(Vector3.zero, "figure far hover");
            Time.frameCount += age;
            Require(!hand.Ray.HasFreshTriggerClaim, "visual world clamp is not committed");
            Require(hand.Ray.HasFreshUiHit == (age <= 1), "world clamp keeps two-frame visual/far-click lifetime");
            Press(hand);
            if (producerFirst) hand.Ray.SetWorldUiHit(Vector3.zero, "world visual on trigger edge");
            hand.Grabber.Tick();
            if (!producerFirst) hand.Ray.SetWorldUiHit(Vector3.zero, "world visual after near acquisition");
            Require(ReferenceEquals(hand.Grabber.Held, figure) && figure.Grabs == 1,
                "highlighted figure survives visual world clamp");
            Require(hand.GrabHaptics == 1, "one acquisition haptic");
            Require(!hand.Grabber.ForceGrab(figure, true), "same edge cannot force-grab twice");
            Require(figure.Grabs == 1, "one acquisition callback only");
            Advance(hand); hand.TriggerPressed = false; hand.Grabber.Tick();
            Require(hand.Grabber.Held == null && figure.Releases == 1, "normal trigger-up releases exactly once");
        }
    }

    private static void CommittedControlsExcludeGlow()
    {
        foreach (int age in new[] { 0, 1, 2 })
        foreach (bool claimFirst in new[] { false, true })
        foreach (string source in new[] { "explicit", "legacy-point", "legacy-suppress" })
        {
            VRHand hand = NewHand(); Figure figure = new(); Add(figure);
            if (!claimFirst) CheckOffer(hand, figure, "before real UI press");
            if (source == "explicit") hand.Ray.ClaimTrigger("active UI drag");
            else if (source == "legacy-point") hand.Ray.UiHitOverride = Vector3.zero;
            else hand.Ray.SuppressFarClick();
            Time.frameCount += age;
            Require(hand.Ray.HasFreshTriggerClaim == (age <= 1), "committed claim has bounded two-frame lifetime");
            Press(hand); hand.Grabber.Tick();
            if (age <= 1)
            {
                Require(!figure.Glow && hand.Grabber.Highlighted == null, "real committed control cannot leave a misleading figure glow");
                Require(!hand.Grabber.FigureGrabOffered && figure.Grabs == 0, "active control keeps trigger without near acquisition");
            }
            else Require(ReferenceEquals(hand.Grabber.Held, figure) && figure.Grabs == 1, "expired claim cannot deadlock later near acquisition");
        }
        VRHand other = NewHand();
        other.Ray.ClaimTrigger("already active control");
        other.Ray.SetPanelUiHit(Vector3.zero, "panel hover after claim");
        other.Ray.SetWorldUiHit(Vector3.zero, "world hover after claim");
        Require(other.Ray.HasFreshTriggerClaim && other.Ray.FreshTriggerClaimSource.Contains("already active control"),
            "later hover cannot erase committed claim provenance");
    }

    private static void FigureGlowEndsWithEligibility()
    {
        foreach (string gate in new[] { "native-busy", "dead", "remote-held", "setting-off", "actor-destroyed", "root-missing", "per-hand", "disabled-shape", "inactive-shape", "destroyed-shape", "unregistered", "out-of-reach", "tracking", "mode" })
        {
            VRHand hand = NewHand(); Figure figure = new(); Collider shape = Add(figure);
            CheckOffer(hand, figure, "before " + gate); Advance(hand);
            switch (gate)
            {
                case "native-busy": figure._actor.Busy = true; break;
                case "dead": figure.Character!.IsDead = true; break;
                case "remote-held": figure._actor.RemoteHeld = true; break;
                case "setting-off": FigureGrabConfig.GrabFigures.Value = false; break;
                case "actor-destroyed": figure._actor.Destroyed = true; break;
                case "root-missing": figure._actor.m_RootGameObject = null; break;
                case "per-hand": figure.SetProximitySuppressed(hand.Side, true); break;
                case "disabled-shape": shape.enabled = false; break;
                case "inactive-shape": shape.gameObject.activeInHierarchy = false; break;
                case "destroyed-shape": shape.Destroyed = true; break;
                case "unregistered": VRInteractables.UnregisterGrabbable(figure); break;
                case "out-of-reach": hand.Rig.PalmCenter.position = new(.15f, .04f, 0); break;
                case "tracking": hand.HasPose = false; break;
                case "mode": hand.Grabber.Enabled = false; break;
            }
            hand.Grabber.Tick();
            Require(!figure.Glow && hand.Grabber.Highlighted == null, "figure eligibility loss clears glow without a misleading tail: " + gate);
            Require(!hand.Grabber.FigureGrabOffered, "invalid figure cannot reserve trigger: " + gate);
            Press(hand); hand.Grabber.Tick();
            Require(figure.Grabs == 0 && hand.Grabber.Held == null, "native and physical safety remain effective: " + gate);
        }
        // Keep the historical hysteresis for nonfigure targets; no global policy relaxation.
        VRHand cardHand = NewHand(); var card = new GloomhavenVR.Cards.VRCard(); Add(card);
        cardHand.Grabber.Tick(); Require(card.Glow, "card starts highlighted");
        Advance(cardHand); card.HandAllowed = false; cardHand.Grabber.Tick();
        Require(card.Glow, "nonfigure hover tail remains unchanged");
        Press(cardHand); cardHand.Grabber.Tick();
        Require(card.Grabs == 0 && !card.Glow, "nonfigure tail still refuses acquisition");
    }

    private static void ScalesAndTallContact()
    {
        VRHand hand = NewHand(); Figure figure = new(); Collider shape = Add(figure);
        for (int cycle = 0; cycle < 30; cycle++)
        foreach (float scale in new[] { .05f, 1f, 50f, .25f, 8f })
        foreach (float y in new[] { 0f, .035f, .4f, .8f })
        {
            hand.Grabber.CancelAll(); figure._holder = null;
            Advance(hand); hand.WorldScale = scale;
            shape.Min = new(-.01f * scale, 0, -.01f * scale);
            shape.Max = new(.01f * scale, .8f * scale, .01f * scale);
            hand.Rig.PalmCenter.position = new(.026f * scale, y * scale, 0);
            CheckOffer(hand, figure, "repeated zoom/tall contact");
            hand.Ray.SetWorldUiHit(Vector3.zero, "distant world visual");
            Press(hand); hand.Grabber.Tick();
            Require(ReferenceEquals(hand.Grabber.Held, figure), "every lit scaled body/bottom contact acquires");
            Advance(hand); hand.TriggerPressed = false; hand.Grabber.Tick();
            Require(hand.Grabber.Held == null, "scaled acquisition releases");
            figure._holder = null;
        }
        Require(figure.Grabs == 600 && figure.Releases == 600, "600 repeated scale/contact cycles have exactly one acquire/release each");
    }

    private static void LiveOfferIsNotAnOldFrameCache()
    {
        foreach (string gate in new[] { "shape-disabled", "native-busy", "remote-held", "zoom-reach", "tracking", "per-hand", "committed" })
        {
            VRHand hand = NewHand(); Figure figure = new(); Collider shape = Add(figure);
            CheckOffer(hand, figure, "before producer-order loss");
            switch (gate)
            {
                case "shape-disabled": shape.enabled = false; break;
                case "native-busy": figure._actor.Busy = true; break;
                case "remote-held": figure._actor.RemoteHeld = true; break;
                case "zoom-reach": hand.WorldScale = .05f; hand.Rig.PalmCenter.position = new(.08f, .04f, 0); break;
                case "tracking": hand.HasPose = false; break;
                case "per-hand": figure.SetProximitySuppressed(hand.Side, true); break;
                case "committed": hand.Ray.ClaimTrigger("pre-existing drag"); break;
            }
            // A rival producer reads the live offer BEFORE this frame's proximity
            // Tick. It must not reserve a dead offer from the preceding frame.
            Require(!hand.Grabber.FigureGrabOffered, "producer reads live eligibility: " + gate);
            Require(!figure.Glow && hand.Grabber.Highlighted == null, "live invalidation also revokes glow: " + gate);
        }
    }

    private static void PreserveOtherInputSafety()
    {
        VRHand hand = NewHand(); Figure figure = new(); Add(figure); CheckOffer(hand, figure, "trigger-only");
        hand.GripDown = hand.GripPressed = true; hand.Grabber.Tick();
        Require(figure.Grabs == 0, "figure never acquired by grip");
        var handle = new Target { GrabWithGrip = true }; Add(handle);
        hand.Grabber.Tick();
        Require(ReferenceEquals(hand.Grabber.Held, handle) && handle.Grabs == 1, "grip independently takes nearby handle");
        Require(!figure.Glow, "grip-bar acquisition cannot leave an unusable figure glowing");
        hand.Grabber.CancelAll(); Require(handle.Cancels == 1, "mode cancellation uses cancellation callback");

        hand = NewHand(); figure = new(); Add(figure); CheckOffer(hand, figure, "hold safety"); Press(hand); hand.Grabber.Tick();
        figure._actor.HoldMustEnd = true; Advance(hand); hand.Grabber.Tick();
        Require(hand.Grabber.Held == null && figure.Cancels == 1, "native non-idle held gate cancels hold");

        hand = NewHand(); figure = new();
        var invalid = new MeshCollider { convex = false }; VRInteractables.RegisterGrabbable(figure, invalid);
        Press(hand); hand.Grabber.Tick();
        Require(!figure.Glow && figure.Grabs == 0, "nonconvex degenerate ClosestPoint cannot admit figure");

        hand = NewHand(); var card = new GloomhavenVR.Cards.VRCard(); Add(card); hand.Grabber.Tick();
        handle = new Target { GrabWithGrip = true }; Add(handle); hand.GripDown = hand.GripPressed = true;
        hand.Grabber.Tick();
        Require(ReferenceEquals(hand.Grabber.Held, handle) && card.Glow, "grip-bar acquisition keeps the original card no-flash behavior");
    }

    private static void ActualFlatScreenPokeEntry()
    {
        foreach (bool screenFirst in new[] { false, true })
        {
            VRHand hand = NewHand(); Figure figure = new(); Add(figure); CheckOffer(hand, figure, "before screen fingertip entry");
            var screen = new GloomhavenVR.WorldUI.FlatScreen();
            VRHands.Left = VRHands.Primary = hand; hand.GripPressed = true; Press(hand);
            if (screenFirst) screen.TickPointerEntry();
            hand.Grabber.Tick();
            if (!screenFirst) screen.TickPointerEntry();
            Require(figure.Grabs == 1 && !screen._pokePressing && screen.Clicks == 0,
                "actual early flat-screen poke cannot steal a lit figure in either update order");
        }
        VRHand pokeHand = NewHand(); Figure pokeFigure = new(); Add(pokeFigure);
        var active = new GloomhavenVR.WorldUI.FlatScreen();
        VRHands.Left = VRHands.Primary = pokeHand; pokeHand.GripPressed = true;
        active.TickPointerEntry();
        Require(active._pokePressing && pokeHand.Ray.HasFreshTriggerClaim, "actual initial fingertip press commits ownership");
        pokeHand.Grabber.Tick(); Require(!pokeFigure.Glow, "active fingertip suppresses new figure glow");
        Time.frameCount += 3; active.TickPointerEntry();
        Require(active._pokePressing && pokeHand.Ray.HasFreshTriggerClaim, "active fingertip renews committed claim");
        pokeHand.Rig.IndexTip.position = new(0, 0, -.04f); active.TickPointerEntry();
        Require(!active._pokePressing && active.Clicks == 1, "normal fingertip withdrawal delivers exactly one click");
        active.TickPointerEntry(); Require(active.Clicks == 1, "withdrawn fingertip cannot duplicate click");

        pokeHand = NewHand(); active = new(); VRHands.Left = VRHands.Primary = pokeHand; pokeHand.GripPressed = true;
        active.TickPointerEntry(); pokeHand.GripPressed = false; active.TickPointerEntry();
        Require(!active._pokePressing && active.Clicks == 0, "grip loss cancels active fingertip without click");
    }

    private static void ActualProducerExclusivity()
    {
        foreach (bool producersFirst in new[] { false, true })
        foreach (HandSide side in Enum.GetValues<HandSide>())
        {
            VRHand hand = NewHand(side); Figure near = new(), far = new();
            Add(near); Collider farShape = Add(far);
            farShape.Min = new(1, 0, 0); farShape.Max = new(1.02f, .08f, .02f);
            var driver = new FigureGrabDriver(); driver.Add(far, farShape); hand.Ray.Pick = new(farShape);
            CheckOffer(hand, near, "before competing producers"); Advance(hand); Press(hand);
            if (producersFirst) { hand.RayUgui.StartPress(); driver.TryLaserGrab(hand); }
            hand.Grabber.Tick();
            if (!producersFirst) { hand.RayUgui.StartPress(); driver.TryLaserGrab(hand); }
            Require(near.Grabs == 1 && far.Grabs == 0 && hand.RayUgui.Presses == 0,
                "actual UI and far-figure entry yield to the lit near figure in either update order");
            Require(ReferenceEquals(hand.Grabber.Held, near), "producer arbitration retains near object identity");
        }
        // An existing UI drag keeps ownership even after the two-frame stamp expires.
        VRHand uiHand = NewHand(); Figure uiFigure = new(); Add(uiFigure);
        Press(uiHand); uiHand.RayUgui.StartPress();
        Require(uiHand.RayUgui.Presses == 1 && uiHand.Ray.HasFreshTriggerClaim, "real UI entry commits one pointer press");
        Time.frameCount += 3; uiHand.Grabber.Tick();
        Require(!uiFigure.Glow && uiFigure.Grabs == 0, "active UI press remains authoritative without fresh timestamp");
        uiHand.RayUgui.EndPress(); uiHand.Grabber.Tick();
        Require(uiFigure.Grabs == 1, "completed UI control does not leave stale near blockade");

        uiHand = NewHand(); uiFigure = new(); Add(uiFigure); CheckOffer(uiHand, uiFigure, "before UI live invalidation");
        uiFigure._actor.Busy = true; Press(uiHand); uiHand.RayUgui.StartPress();
        Require(uiHand.RayUgui.Presses == 1 && !uiFigure.Glow, "invalid live figure offer does not swallow actual UI press");

        VRHand released = NewHand(); Figure oldNear = new(), otherFar = new(); Add(oldNear);
        Collider otherShape = Add(otherFar); otherShape.Min = new(1, 0, 0); otherShape.Max = new(1.02f, .08f, .02f);
        var releasedDriver = new FigureGrabDriver(); releasedDriver.Add(otherFar, otherShape); released.Ray.Pick = new(otherShape);
        CheckOffer(released, oldNear, "before far live invalidation"); oldNear._actor.Busy = true;
        Press(released); releasedDriver.TryLaserGrab(released);
        Require(otherFar.Grabs == 1 && !oldNear.Glow, "invalid near glow cannot block a deliberate far pickup");

        VRHand farHand = NewHand(); Figure farFigure = new(); Collider shape = Add(farFigure);
        farHand.Rig.PalmCenter.position = new(2, 0, 0);
        var farDriver = new FigureGrabDriver(); farDriver.Add(farFigure, shape); farHand.Ray.Pick = new(shape);
        farFigure.SetProximitySuppressed(farHand.Side, true);
        Press(farHand); farDriver.TryLaserGrab(farHand); farHand.Grabber.Tick();
        Require(farFigure.Grabs == 1 && farHand.Ray.HasFreshTriggerClaim && ReferenceEquals(farHand.Grabber.Held, farFigure),
            "actual deliberate far pickup still clears its exact target veto and commits once");
        farDriver.TryLaserGrab(farHand);
        Require(farFigure.Grabs == 1, "far producer cannot duplicate an existing hold");

        farHand = NewHand(); farFigure = new(); shape = Add(farFigure);
        farHand.Rig.PalmCenter.position = new(2, 0, 0);
        farDriver = new(); farDriver.Add(farFigure, shape); farHand.Ray.Pick = new(shape);
        FigureStretch.Captured[(int)farHand.Side] = true; Press(farHand);
        farDriver.TryLaserGrab(farHand); farDriver.SelectByOffsetAnchor(farHand); farHand.Grabber.Tick();
        Require(farFigure.Grabs == 0 && !farFigure.Glow, "active stretch keeps far grab and near election excluded");
        FigureStretch.Captured[(int)farHand.Side] = false;
    }

    private static void NativeColliderAndPreviousWinner()
    {
        foreach (string gate in new[] { "disabled", "inactive", "destroyed", "nonconvex" })
        {
            VRHand hand = NewHand(); var original = new MeshCollider(); Figure figure = new(new NativeActor(), original);
            Collider extension = Add(figure);
            CheckOffer(hand, figure, "usable original under extension");
            if (gate == "disabled") original.enabled = false;
            else if (gate == "inactive") original.gameObject.activeInHierarchy = false;
            else if (gate == "destroyed") original.Destroyed = true;
            else original.convex = false;
            Require(VRInteractables.IsUsablePickShape(extension), "reach extension remains usable in native veto case");
            Require(!figure.CanGrab, "real CanGrab retains original native collider veto under usable extension");
            Require(!hand.Grabber.FigureGrabOffered && !figure.Glow, "native collider veto revokes live visible offer");
            Press(hand); hand.Grabber.Tick(); Require(figure.Grabs == 0, "native original veto blocks pickup");
        }
        Figure optional = new();
        Require(optional.CanGrab, "optional no-original-collider construction retains prior admissibility");

        foreach (string gate in new[] { "disabled-native", "busy", "remote", "out-of-reach" })
        {
            VRHand hand = NewHand(); var native = new Collider(); Figure previous = new(new NativeActor(), native), next = new();
            Collider previousShape = Add(previous), nextShape = Add(next);
            nextShape.Min = new(.04f, 0, 0); nextShape.Max = new(.06f, .08f, .02f);
            var driver = new FigureGrabDriver(); var adoption = driver.Add(previous, previousShape);
            adoption.GameCollider = native; adoption.Reach = new object(); driver.Add(next, nextShape);
            for (int frame = 0; frame < 6; frame++) { driver.SelectByOffsetAnchor(hand); Advance(hand); }
            CheckOffer(hand, previous, "driver dwell initial winner");
            if (gate == "disabled-native") native.enabled = false;
            else if (gate == "busy") previous._actor.Busy = true;
            else if (gate == "remote") previous._actor.RemoteHeld = true;
            else { previousShape.Min = new(-1, 0, 0); previousShape.Max = new(-.98f, .08f, .02f); }
            driver.SelectByOffsetAnchor(hand);
            Require(previous._suppressLeft, "switching dwell never reallows an ineligible old winner");
            Require(next._suppressLeft, "new winner must still earn ordinary arrival dwell");
            hand.Grabber.Tick(); Require(!previous.Glow && !next.Glow, "driver suppression and visible grab promise agree during arrival dwell");
            for (int frame = 0; frame < 5; frame++) { driver.SelectByOffsetAnchor(hand); Advance(hand); }
            CheckOffer(hand, next, "driver dwell admits replacement after required frames");
        }
    }

    private static void ActualBoardClickExclusivity()
    {
        foreach (bool boardFirst in new[] { false, true })
        {
            VRHand hand = NewHand(); Figure figure = new(); Add(figure); CheckOffer(hand, figure, "before native board trigger");
            GloomhavenVR.Board.BoardPick.SourceHand = hand;
            GloomhavenVR.Board.BoardClickDriver.ConsumePendingClick();
            Press(hand);
            if (boardFirst) GloomhavenVR.Board.BoardClickDriver.TickFar();
            hand.Grabber.Tick();
            if (!boardFirst) GloomhavenVR.Board.BoardClickDriver.TickFar();
            Require(figure.Grabs == 1 && !GloomhavenVR.Board.BoardClickDriver.ConsumePendingClick(),
                "actual board trigger cannot duplicate highlighted near pickup in either update order");
        }
        VRHand ordinary = NewHand(); GloomhavenVR.Board.BoardPick.SourceHand = ordinary;
        Press(ordinary); GloomhavenVR.Board.BoardClickDriver.TickFar();
        Require(GloomhavenVR.Board.BoardClickDriver.ConsumePendingClick(), "ordinary far-board trigger still requests native click");
        Require(!GloomhavenVR.Board.BoardClickDriver.ConsumePendingClick(), "native board bridge consumes pending click once");
    }

    private static int Main()
    {
        try
        {
            WorldHoverDoesNotVeto(); CommittedControlsExcludeGlow(); FigureGlowEndsWithEligibility();
            ScalesAndTallContact(); LiveOfferIsNotAnOldFrameCache(); PreserveOtherInputSafety();
            ActualProducerExclusivity(); NativeColliderAndPreviousWinner(); ActualFlatScreenPokeEntry(); ActualBoardClickExclusivity();
            Console.WriteLine("PASS near figure grab: " + _assertions + " assertions; production election, callback and claim kernels; portable boundaries");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}

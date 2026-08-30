using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// "VR" AS ITS OWN ENTRY IN THE PAUSE MENU, one level up from where it was — a sibling of
/// Fortsetzen / Optionen / Kompendium rather than a tab buried inside Optionen (user, 2026-08-30:
/// "das die VR Optionen direkt auf einer Ebene mit den Optionen sind im Pausenmenu erreichbar
/// statt ein Tab in den Optionen selber").
///
/// <para>A CLONE OF THE OPTIONS BUTTON, for exactly the reasons <see cref="VROptionsTab"/> clones a
/// tab: an entry here must join the same <c>ToggleGroup</c> (so picking it deselects the others),
/// animate like its neighbours, carry the same hover and focus wiring, and be reachable by the
/// gamepad navigation the menu builds over its children. All of that lives in serialized references
/// inside the shipped prefab, so the row is INSTANTIATED FROM THE LIVE OPTIONS BUTTON and
/// re-labelled. Nothing about the donor is modified, and it is inserted directly after it so the
/// two read as a pair.</para>
///
/// <para>ONE INJECTION COVERS BOTH PAUSE MENUS. <c>UIMapEscMenu</c> and <c>UIScenarioEscMenu</c>
/// are subclasses of <c>ESCMenu</c> and the <c>optionsButton</c> field is on the base, so the map
/// room and a scenario are the same code path — and because the singleton instance changes with
/// the scene, <see cref="Tick"/> re-injects when it sees a different one rather than assuming its
/// clone survived.</para>
///
/// <para><b>THE TAB STAYS, AND THAT IS DELIBERATE.</b> Two reasons, and neither is inertia. First,
/// the tab is the ONLY route from the MAIN menu — the main menu is not an <c>ESCMenu</c> at all
/// (its list is <c>GLOOM.MainMenu.UIMainOptionsMenu.menuOptions</c>), so deleting the tab would
/// make every VR setting unreachable before a game is loaded. Second, the standing ruling is that
/// the settings must never become unreachable; a top-level row that fails to inject after a game
/// update would do exactly that, and the tab is what makes that failure survivable. The entry is
/// the front door, the tab is the fire exit.</para>
///
/// <para>WHAT IT OPENS. The game's own options window, with the VR tab selected — not a
/// mod-built window. That keeps one settings surface rather than two that can disagree, and it
/// inherits the whole VR modal path the options window already has (<see cref="ModalFallback"/>
/// carries <c>UIOptionsWindow</c>), so this class adds no rendering, no placement and no input.
/// </para>
///
/// <para>REVERSIBILITY. The only thing that exists is one mod-owned clone parented into the game's
/// hierarchy. <see cref="Shutdown"/> destroys it and the menu is byte-for-byte as it shipped.</para>
/// </summary>
internal static class VRMenuEntry
{
    private const string CloneName = "GloomhavenVR.PauseMenuEntry";

    /// <summary>The menu we are injected into (null = not injected). Compared by reference every
    /// tick: a new scene brings a new ESCMenu instance and our clone died with the old one.</summary>
    private static ESCMenu? _host;
    private static UIMainMenuOption? _entry;
    private static bool _loggedInject;
    private static bool _loggedIcon;
    private static bool _degraded;

    /// <summary>The mod's own VR emblem: a gold woodcut headset, generated for this menu and
    /// trimmed to what it DRAWS rather than to its frame (a mostly-transparent logo aligned to its
    /// frame has shipped 2.35x too wide in this project before).</summary>
    private const string IconAsset = "Assets/Bundle/UI/VRMenuIcon.png";

    /// <summary>Per-frame step from <see cref="WorldUIModule"/>. Cheap: one singleton read while
    /// injected, and it never searches the scene.</summary>
    internal static void Tick()
    {
        if (_degraded)
            return;
        try
        {
            ESCMenu? host = Singleton<ESCMenu>.IsInitialized ? Singleton<ESCMenu>.Instance : null;
            if (host == null)
            {
                // The menu went away with its scene; our clone went with it.
                _host = null;
                _entry = null;
                return;
            }
            if (ReferenceEquals(host, _host) && _entry != null)
                return;
            _host = host;
            _entry = null;
            Inject(host);
        }
        catch (Exception ex)
        {
            // A settings entry must never be able to take the pause menu down with it. Disarm for
            // the session and say so once, with the stack: the tab is still there, so the player
            // keeps every setting either way.
            _degraded = true;
            VRLog.Error("WorldUI", "The pause-menu VR entry threw and is disabled for this "
                + "session; the VR tab inside Optionen is unaffected and still carries every "
                + $"setting. {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void Inject(ESCMenu host)
    {
        UIMainMenuOption? donor = host.optionsButton;
        if (donor == null)
        {
            VRLog.Warn("WorldUI", "Pause menu has no options button to clone from — the VR entry "
                + "is not added. Every setting stays reachable through the VR tab inside Optionen.");
            return;
        }

        // A clone from a previous injection into THIS instance (hot reload) would otherwise stack.
        Transform? stale = donor.transform.parent != null
            ? donor.transform.parent.Find(CloneName) : null;
        if (stale != null)
            UnityEngine.Object.Destroy(stale.gameObject);

        var clone = UnityEngine.Object.Instantiate(donor.gameObject, donor.transform.parent)
            .GetComponent<UIMainMenuOption>();
        if (clone == null)
        {
            VRLog.Warn("WorldUI", "The pause-menu options button cloned without its "
                + "UIMainMenuOption — the VR entry is not added.");
            return;
        }

        clone.name = CloneName;
        // Directly BELOW Optionen, so the pair reads as "settings, and the VR ones".
        clone.transform.SetSiblingIndex(donor.transform.GetSiblingIndex() + 1);
        clone.gameObject.SetActive(true);

        // The donor's localisation listeners would overwrite our caption with the German for
        // "Optionen" the first time the player switches language. Same removal the tab does.
        foreach (TextLocalizedListener listener in clone.GetComponentsInChildren<TextLocalizedListener>(true))
            UnityEngine.Object.Destroy(listener);

        VROptionsTab.SetCaption(clone, Loc.Mod("vr_options"));
        ApplyIcon(clone);
        clone.IsInteractable = true;

        // The two halves mirror ESCMenu's own wiring for the options button (ESCMenu.cs:135-146):
        // selecting unfocuses the menu and shows the window anchored at OUR row; deselecting hides
        // it and gives focus back. The one addition is selecting the VR tab once it is up.
        clone.Init(
            delegate
            {
                SetFocused(host, false);
                if (Singleton<UIOptionsWindow>.IsInitialized)
                {
                    Singleton<UIOptionsWindow>.Instance.Show(
                        clone.transform as RectTransform, clone.Deselect);
                    VROptionsTab.SelectTab();
                }
            },
            delegate
            {
                if (Singleton<UIOptionsWindow>.IsInitialized)
                    Singleton<UIOptionsWindow>.Instance.Hide();
                SetFocused(host, true);
            });

        _entry = clone;
        if (!_loggedInject)
        {
            _loggedInject = true;
            VRLog.Info("WorldUI", $"VR is now its OWN pause-menu entry, directly under "
                + $"'{donor.name}' (label '{Loc.Mod("vr_options")}'). It opens the game's options "
                + "window with the VR tab already selected. The tab itself stays: it is the only "
                + "route from the MAIN menu, which is not an ESCMenu, and it is the fallback if "
                + "this row ever fails to inject.");
        }
    }

    /// <summary>
    /// Give the row the mod's own VR emblem — BUT ONLY IF THE DONOR ROW HAS AN ICON TO REPLACE.
    ///
    /// <para>This machine cannot see the game's menu art: only the Managed DLLs are on disk, and
    /// <c>UIMainMenuOption</c> itself declares no icon field — just a TMP label, a focus mask and a
    /// locked mask. Whether the shipped ROW carries an icon Image beside its text is authored
    /// prefab data, and therefore not a question that can be answered here. So this does not
    /// decide; it LOOKS. Exactly one image with a sprite that is neither mask means the row has an
    /// icon and ours goes in its place. Zero, or several, means it does not — and the row stays
    /// text-only like every one of its neighbours, which is the whole point: an icon nobody else
    /// has would not match the menu, it would break it.</para>
    ///
    /// <para>Either outcome is logged once, so the next hardware run answers the question that
    /// could not be answered from here.</para>
    /// </summary>
    private static void ApplyIcon(UIMainMenuOption clone)
    {
        try
        {
            var candidates = new List<UnityEngine.UI.Image>(2);
            foreach (UnityEngine.UI.Image image in
                     clone.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image == null || image.sprite == null)
                    continue;
                if (ReferenceEquals(image, clone.focusMask))
                    continue;
                candidates.Add(image);
            }

            if (candidates.Count != 1)
            {
                if (!_loggedIcon)
                {
                    _loggedIcon = true;
                    VRLog.Info("WorldUI", $"Pause-menu rows carry {candidates.Count} sprite "
                        + "image(s) besides the focus mask, so there is no single icon slot to "
                        + "fill: the VR row stays TEXT-ONLY, matching its neighbours. The "
                        + "generated emblem is in the bundle "
                        + $"({IconAsset}) and is used the moment a row turns out to have one.");
                }
                return;
            }

            Sprite? icon = WorldUIAssets.TryLoadSprite(IconAsset);
            if (icon == null)
                return;
            candidates[0].sprite = icon;
            if (!_loggedIcon)
            {
                _loggedIcon = true;
                VRLog.Info("WorldUI", "Pause-menu rows DO carry an icon, so the VR row wears the "
                    + $"mod's own emblem ({IconAsset}) in place of the cloned one.");
            }
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"Could not set the VR row's icon ({ex.GetType().Name}); the row "
                + "is otherwise complete.");
        }
    }

    /// <summary>ESCMenu.SetFocused is the game's own focus handoff. Guarded because it is reached
    /// through a publicised member and a game update could change or drop it — losing the focus
    /// handoff is a cosmetic regression, losing the entry is not.</summary>
    private static void SetFocused(ESCMenu host, bool focused)
    {
        try
        {
            host.SetFocused(focused);
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"Pause-menu focus handoff failed ({ex.GetType().Name}); the VR "
                + "entry still opens and closes the window.");
        }
    }

    /// <summary>Module teardown: drop the clone, leaving the menu exactly as it shipped.</summary>
    internal static void Shutdown()
    {
        if (_entry != null)
            UnityEngine.Object.Destroy(_entry.gameObject);
        _entry = null;
        _host = null;
        _loggedInject = false;
        _loggedIcon = false;
        _degraded = false;
    }
}

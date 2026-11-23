using BetterHammer.BuildMenu;

using HarmonyLib;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterHammer.Patches;

/// <summary>
/// Hooks the build menu up to <see cref="SectionedGrid"/>. The game keeps creating and filling the piece
/// buttons; these patches move them into sections afterwards and take over the parts of the menu that
/// assumed every button sits in one grid (finding the first piece, selecting a piece, navigation). With
/// sections turned off, every patch leaves the game's code alone.
/// </summary>
[HarmonyPatch(typeof(BuildUi))]
internal static class BuildUiPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    private static void Awake_Postfix(BuildUi __instance)
    {
        SectionedGrid.Attach(__instance);
    }

    /// <summary>
    /// Sizes the menu each time it opens, in case the screen or GUI scale changed.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(BuildUi.OpenBuildMenu))]
    private static void OpenBuildMenu_Postfix(BuildUi __instance)
    {
        if (SectionedGrid.For(__instance) != null)
        {
            MenuLayout.Apply(__instance);
        }
    }

    /// <summary>
    /// Right-click is the game's key for closing the build menu. Over one of the player's favourites
    /// categories it renames the category instead (see <see cref="TagDrag"/>), so the menu stays open.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("NavigationUpdate")]
    private static void NavigationUpdate_Prefix(BuildUi __instance)
    {
        if (SectionedGrid.For(__instance) != null && Input.GetMouseButtonDown(1) && TagDrag.IsRenameTarget(__instance, __instance.m_currentHoveredTagButton))
        {
            ZInput.ResetButtonStatus("BuildMenu");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch("UpdateTagButtons")]
    private static void UpdateTagButtons_Prefix(BuildUi __instance)
    {
        SectionedGrid.For(__instance)?.TagHeaders.Park();
    }

    [HarmonyPostfix]
    [HarmonyPatch("UpdateTagButtons")]
    private static void UpdateTagButtons_Postfix(BuildUi __instance)
    {
        SectionedGrid grid = SectionedGrid.For(__instance);
        grid?.TagHeaders.Place();
        grid?.HookTagButtons();
    }

    [HarmonyPrefix]
    [HarmonyPatch("UpdatePieceButtons")]
    private static void UpdatePieceButtons_Prefix(BuildUi __instance)
    {
        SectionedGrid.For(__instance)?.BeginUpdate();
    }

    [HarmonyFinalizer]
    [HarmonyPatch("UpdatePieceButtons")]
    private static void UpdatePieceButtons_Finalizer(BuildUi __instance)
    {
        SectionedGrid.For(__instance)?.EndUpdate();
    }

    [HarmonyPostfix]
    [HarmonyPatch("UpdateSearch")]
    private static void UpdateSearch_Postfix(BuildUi __instance)
    {
        SectionedGrid.For(__instance)?.Refresh();
    }

    [HarmonyPrefix]
    [HarmonyPatch("ConfigureButtonNavigation")]
    private static bool ConfigureButtonNavigation_Prefix(BuildUi __instance)
    {
        SectionedGrid grid = SectionedGrid.For(__instance);
        if (grid == null)
        {
            return true;
        }

        GridNavigationUtility.ConfigureExplicitNavigation(__instance.m_tagListScrollContent, __instance.GetTagListEdgeNavigation());
        if (__instance.m_specialPieceButton != null)
        {
            __instance.m_specialPieceButton.GetComponent<Button>().navigation = __instance.GetSpecialButtonNavigation();
        }

        if (!grid.Updating)
        {
            grid.ConfigureNavigation(__instance.GetPieceListEdgeNavigation().selectOnLeft);
        }

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("GetFirstPieceButton")]
    private static bool GetFirstPieceButton_Prefix(BuildUi __instance, ref GameObject __result)
    {
        SectionedGrid grid = SectionedGrid.For(__instance);
        if (grid == null)
        {
            return true;
        }

        __result = grid.FirstButton();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("SelectFirstPiece")]
    private static bool SelectFirstPiece_Prefix(BuildUi __instance)
    {
        SectionedGrid grid = SectionedGrid.For(__instance);
        if (grid == null)
        {
            return true;
        }

        _ = grid.SelectFirst();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("SelectFirstAvailableButton")]
    private static bool SelectFirstAvailableButton_Prefix(BuildUi __instance)
    {
        SectionedGrid grid = SectionedGrid.For(__instance);
        if (grid == null)
        {
            return true;
        }

        if (__instance.m_tagListContainer.gameObject.activeSelf)
        {
            __instance.m_showAllTagsButton.GetComponent<Selectable>().Select();
        }
        else
        {
            GameObject first = grid.FirstButton();
            if (first != null)
            {
                first.GetComponent<Selectable>().Select();
            }
        }

        return false;
    }

    /// <summary>
    /// Selects the button of the piece being built when the menu opens, else the first piece.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("UpdatePieceSelection")]
    private static bool UpdatePieceSelection_Prefix(BuildUi __instance, Piece selectedPiece)
    {
        SectionedGrid grid = SectionedGrid.For(__instance);
        if (grid == null)
        {
            return true;
        }

        if (selectedPiece != null)
        {
            string name = selectedPiece.gameObject.name;
            foreach (BuildUiPieceButton button in grid.ShownButtons())
            {
                if (button.Piece.gameObject.name == name)
                {
                    EventSystem.current.SetSelectedGameObject(button.gameObject);
                    return false;
                }
            }
        }

        if (!grid.SelectFirst() && __instance.m_tagListContainer.gameObject.activeSelf)
        {
            EventSystem.current.SetSelectedGameObject(__instance.m_showAllTagsButton.gameObject);
        }

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnFavoritePieceAdded")]
    private static void OnFavoritePieceAdded_Postfix(BuildUi __instance, string pieceName)
    {
        SectionedGrid.For(__instance)?.RefreshFavorite(pieceName);
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnFavoritePieceRemoved")]
    private static void OnFavoritePieceRemoved_Postfix(BuildUi __instance, string pieceName)
    {
        SectionedGrid.For(__instance)?.RefreshFavorite(pieceName);
    }
}

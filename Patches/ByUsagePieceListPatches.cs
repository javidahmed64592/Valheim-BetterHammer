using System.Collections.Generic;

using BetterHammer.BuildMenu;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// The Categories tab's list: categories in groups, Modded and Hidden categories, and each category showing exactly
/// the pieces in it. Runs after Jotunn, which adds other mods' categories the same way. With sections
/// turned off, the game's list is left alone.
/// </summary>
[HarmonyPatch(typeof(ByUsagePieceList))]
internal static class ByUsagePieceListPatches
{
    private const string JotunnGuid = "com.jotunn.jotunn";

    [HarmonyPostfix]
    [HarmonyAfter(JotunnGuid)]
    [HarmonyPatch(nameof(ByUsagePieceList.UpdateAvailableTags))]
    private static void UpdateAvailableTags_Postfix(ByUsagePieceList __instance, PieceTable pieceTable)
    {
        if (BuildMenuConfig.Sections)
        {
            CategoryTags.Update(__instance, pieceTable);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(ByUsagePieceList.GetTagDisplayName))]
    private static bool GetTagDisplayName_Prefix(ByUsagePieceList __instance, int index, ref string __result)
    {
        if (index < 0 || index >= __instance.m_availableTags.Count || !PieceCategories.IsOwnTag(__instance.m_availableTags[index]))
        {
            return true;
        }

        __result = __instance.m_availableTags[index] == PieceCategories.ModdedTagId ? "$betterhammer_menu_modded" : "$betterhammer_menu_hidden";
        return false;
    }

    /// <summary>
    /// The Modded and Hidden categories are filled here: the game would look their ids up as usage tags.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ByUsagePieceList.GetAvailablePiecesWithTag))]
    private static bool GetAvailablePiecesWithTag_Prefix(int tagId, PieceTable pieceTable, IList<Piece> resultOut)
    {
        if (!PieceCategories.IsOwnTag(tagId))
        {
            return true;
        }

        CategoryTags.AddOwnPieces(tagId, pieceTable, resultOut);
        return false;
    }

    [HarmonyPostfix]
    [HarmonyAfter(JotunnGuid)]
    [HarmonyPatch(nameof(ByUsagePieceList.GetAvailablePiecesWithTag))]
    private static void GetAvailablePiecesWithTag_Postfix(ByUsagePieceList __instance, int tagId, PieceTable pieceTable, IList<Piece> resultOut)
    {
        if (BuildMenuConfig.Sections && pieceTable != null && PieceCategories.IsSortedTag(__instance, tagId))
        {
            CategoryTags.Filter(__instance, tagId, pieceTable, resultOut);
        }
    }
}

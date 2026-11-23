using System.Collections.Generic;

using BetterHammer.BuildMenu;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// The Categories tab's list: categories in groups, a Modded category, and each category showing exactly
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
        if (index < 0 || index >= __instance.m_availableTags.Count || __instance.m_availableTags[index] != PieceCategories.ModdedTagId)
        {
            return true;
        }

        __result = "$betterhammer_menu_modded";
        return false;
    }

    /// <summary>
    /// The Modded category is filled here: the game would look its id up as a usage tag.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ByUsagePieceList.GetAvailablePiecesWithTag))]
    private static bool GetAvailablePiecesWithTag_Prefix(int tagId, PieceTable pieceTable, IList<Piece> resultOut)
    {
        if (tagId != PieceCategories.ModdedTagId)
        {
            return true;
        }

        CategoryTags.AddModdedPieces(pieceTable, resultOut);
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

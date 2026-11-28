using BetterHammer.BuildMenu;
using BetterHammer.Core;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// The Materials tab's list of materials, in biome order. With sections turned off, the game's order is
/// kept.
/// </summary>
[HarmonyPatch(typeof(ByMaterialPieceList))]
[Feature(SectionedGrid.Feature)]
internal static class ByMaterialPieceListPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ByMaterialPieceList.UpdateAvailableTags))]
    private static void UpdateAvailableTags_Postfix(ByMaterialPieceList __instance)
    {
        if (BuildMenuConfig.Sections)
        {
            MaterialTags.Sort(__instance);
        }
    }
}

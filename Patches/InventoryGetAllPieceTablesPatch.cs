using System.Collections.Generic;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Includes the phantom hammer's piece table in every inventory scan so
/// UpdateKnownRecipesList discovers its pieces even without a real hammer.
/// </summary>
[HarmonyPatch(typeof(Inventory), nameof(Inventory.GetAllPieceTables))]
internal static class Inventory_GetAllPieceTables_Patch
{
    [HarmonyPostfix]
    private static void Postfix(List<PieceTable> tables)
    {
        ItemDrop.ItemData phantom = BetterHammerPlugin._phantomHammer;
        if (phantom?.m_shared?.m_buildPieces != null
            && !tables.Contains(phantom.m_shared.m_buildPieces))
        {
            tables.Add(phantom.m_shared.m_buildPieces);
        }
    }
}

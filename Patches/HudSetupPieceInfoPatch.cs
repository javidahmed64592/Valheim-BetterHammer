using BetterHammer.Structures;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// The build panel describes the pieces selected for copying (while Alt is held) or a saved copy, with
/// their combined materials a page at a time, instead of the piece itself.
/// </summary>
[HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
internal static class Hud_SetupPieceInfo_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref Piece piece)
    {
        if (MultiCopy.TryGetSummary(piece, out Piece summary))
        {
            piece = summary;
        }
    }
}

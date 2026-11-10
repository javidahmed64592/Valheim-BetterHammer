using HarmonyLib;

namespace NoHammerBuildMenu.Patches;

/// <summary>
/// The build panel describes the pieces selected for copying (while Alt is held) or the copy being
/// placed, with their combined materials, instead of the selected piece.
/// </summary>
[HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
internal static class Hud_SetupPieceInfo_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref Piece piece)
    {
        if (MultiCopy.TryGetSummary(out Piece summary))
        {
            piece = summary;
        }
    }
}

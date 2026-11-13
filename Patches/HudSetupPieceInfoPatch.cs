using BetterHammer.Structures;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// The build panel describes the pieces selected for copying (while Alt is held) or a saved copy, with
/// their combined materials a page at a time and every crafting station they need, instead of the piece
/// itself.
/// </summary>
[HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
internal static class Hud_SetupPieceInfo_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref Piece piece, out Piece __state)
    {
        __state = null;
        if (MultiCopy.TryGetSummary(piece, out Piece summary))
        {
            piece = summary;
            __state = summary;
        }
    }

    /// <summary>
    /// The game shows one crafting station; a copy can need several.
    /// </summary>
    [HarmonyPostfix]
    private static void Postfix(Hud __instance, Piece __state)
    {
        if (__state != null)
        {
            MultiCopy.AfterSetupPieceInfo(__instance, __state);
        }
    }
}

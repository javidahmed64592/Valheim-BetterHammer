using BetterHammer.Structures;
using BetterHammer.UI;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// The build panel gets room for more slots (see <see cref="BuildPanel"/>), and describes the pieces
/// selected for copying (while Alt is held) or a saved copy, with their combined materials a page at a time
/// and every crafting station they need, instead of the piece itself. It's only wider than the game's when
/// a copy or selection needs it.
/// </summary>
[HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
internal static class Hud_SetupPieceInfo_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Hud __instance, ref Piece piece, out Piece __state)
    {
        BuildPanel.BeforeSetupPieceInfo(__instance);
        __state = null;
        if (MultiCopy.TryGetSummary(piece, out Piece summary))
        {
            piece = summary;
            __state = summary;
        }

        BuildPanel.SetWidth(__state != null ? MultiCopy.PanelWidth : BuildPanel.BaseSlots);
    }

    /// <summary>
    /// The game shows one crafting station after the materials; a copy's stations are shown at the
    /// panel's right end instead, as many as it needs.
    /// </summary>
    [HarmonyPostfix]
    private static void Postfix(Hud __instance, Piece __state)
    {
        if (__state != null)
        {
            MultiCopy.AfterSetupPieceInfo(__instance);
        }
    }
}

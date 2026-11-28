using BetterHammer.Core;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// The game copies the hovered piece on Shift+Middle-Click, which is also part of Alt+Shift+Middle-Click (select
/// connected pieces). While Alt is held with a hammer out, the click is the selection's, so the game doesn't copy.
/// </summary>
[HarmonyPatch(typeof(Player), "CopyPiece")]
[Feature(Feature)]
internal static class Player_CopyPiece_SelectPatch
{
    /// <summary>Without it, selecting connected pieces also copies the hovered piece.</summary>
    public const string Feature = "Selecting doesn't copy";

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Player __instance, ref bool __result)
    {
        if (!BetterHammerPlugin.SelectionInput(__instance))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

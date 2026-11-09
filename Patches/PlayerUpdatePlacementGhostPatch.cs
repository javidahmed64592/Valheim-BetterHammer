using System;

using HarmonyLib;

namespace NoHammerBuildMenu.Patches;

/// <summary>
/// While moving a piece, hides the original from the ghost's placement checks so it can be put down
/// overlapping its old spot. The finalizer always puts it back, even if the method throws.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class Player_UpdatePlacementGhost_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
        {
            PieceMover.HideFromPlacementChecks();
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        PieceMover.RestoreAfterPlacementChecks();
        return __exception;
    }
}

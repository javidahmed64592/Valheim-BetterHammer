using System;

using HarmonyLib;

namespace NoHammerBuildMenu.Patches;

/// <summary>
/// While moving a piece, hides the original from the ghost's placement checks so it can be put down
/// overlapping its old spot. The finalizer always puts it back, even if the method throws. While placing
/// a copy of several pieces, handles snap point cycling and places the other pieces' ghosts.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class Player_UpdatePlacementGhost_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
        {
            MultiCopy.BeforeUpdatePlacementGhost(__instance);
            PieceMover.HideFromPlacementChecks();
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Player __instance, bool flashGuardStone)
    {
        if (__instance == Player.m_localPlayer)
        {
            MultiCopy.AfterUpdatePlacementGhost(__instance, flashGuardStone);
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        PieceMover.RestoreAfterPlacementChecks();
        return __exception;
    }
}

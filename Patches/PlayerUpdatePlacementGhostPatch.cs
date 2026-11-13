using System;

using BetterHammer.Structures;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// While moving a piece, hides the original from the ghost's placement checks so it can be put down
/// overlapping its old spot. The finalizer always puts it back, even if the method throws. While placing
/// several pieces at once (a saved copy, or moving a selection), handles snap point cycling and places
/// the other pieces' ghosts. While typing (a copy's name, a sign, the build menu's search box), Q and E
/// are letters, so they don't cycle snap points; the game itself only skips them for the console and chat.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class Player_UpdatePlacementGhost_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance, out int? __state)
    {
        __state = null;
        if (__instance == Player.m_localPlayer)
        {
            if (CopyLibrary.TypingInTextField())
            {
                // The game reads the snap point before applying Q/E, so putting it back afterwards
                // undoes the step without the ghost jumping for a frame.
                __state = __instance.m_manualSnapPoint;
            }
            else
            {
                GhostGroup.BeforeUpdatePlacementGhost(__instance);
            }

            PieceMover.HideFromPlacementChecks();
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Player __instance, bool flashGuardStone, int? __state)
    {
        if (__state.HasValue)
        {
            __instance.m_manualSnapPoint = __state.Value;
        }

        if (__instance == Player.m_localPlayer)
        {
            GhostGroup.AfterUpdatePlacementGhost(__instance, flashGuardStone);
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        PieceMover.RestoreAfterPlacementChecks();
        return __exception;
    }
}

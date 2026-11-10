using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

namespace BetterHammer.Patches;

/// <summary>
/// Whenever the game sets up the placement ghost (a new piece selected, the hammer equipped), a saved
/// copy that's selected gets its own ghosts.
/// </summary>
[HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
internal static class Player_SetupPlacementGhost_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
        {
            MultiCopy.AfterSetupPlacementGhost(__instance);
        }
    }
}

/// <summary>
/// Lets a ghost group have the game set up a ghost for each of its pieces while the selected piece stays
/// what it is.
/// </summary>
[HarmonyPatch(typeof(PieceTable), nameof(PieceTable.GetSelectedPrefab))]
internal static class PieceTable_GetSelectedPrefab_Patch
{
    [HarmonyPostfix]
    private static void Postfix(ref GameObject __result)
    {
        if (GhostGroup.PrefabOverride != null)
        {
            __result = GhostGroup.PrefabOverride;
        }
    }
}

/// <summary>
/// Saved copies are always available, without being added to the character's known recipes (which are
/// saved with the character).
/// </summary>
[HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
internal static class PieceTable_UpdateAvailable_Patch
{
    [HarmonyPrefix]
    private static void Prefix(HashSet<string> knownRecipies, out List<string> __state)
    {
        __state = [];
        foreach (string name in CopyLibrary.AllNames)
        {
            if (knownRecipies.Add(name))
            {
                __state.Add(name);
            }
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, HashSet<string> knownRecipies, List<string> __state)
    {
        if (__state != null)
        {
            foreach (string name in __state)
            {
                _ = knownRecipies.Remove(name);
            }
        }

        return __exception;
    }
}

/// <summary>
/// No "new piece" message for saved copies, and they stay out of the character's known recipes.
/// </summary>
[HarmonyPatch(typeof(Player), "AddKnownPiece")]
internal static class Player_AddKnownPiece_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Piece piece)
    {
        return piece == null || !CopyLibrary.IsCopyName(piece.m_name);
    }
}

/// <summary>
/// A saved copy's menu entry is only a stand-in: it's built by the copy feature, never placed (and paid
/// for) as a piece itself, e.g. with a gamepad, where the copy feature's mouse input doesn't run.
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
internal static class Player_TryPlacePiece_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Piece piece, ref bool __result)
    {
        if (CopyLibrary.Find(piece) == null)
        {
            return true;
        }

        __result = false;
        return false;
    }
}

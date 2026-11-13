using BetterHammer.History;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Notes the pieces the local player builds, so building them can be undone (see
/// <see cref="UndoHistory"/>). Player.PlacePiece doesn't return what it made, but it sets the new piece's
/// creator.
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
internal static class Player_PlacePiece_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance, out bool __state)
    {
        __state = __instance == Player.m_localPlayer;
        if (__state)
        {
            UndoHistory.BeforePlacePiece();
        }
    }

    [HarmonyFinalizer]
    private static System.Exception Finalizer(System.Exception __exception, bool __state)
    {
        if (__state)
        {
            UndoHistory.AfterPlacePiece();
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
internal static class Piece_SetCreator_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Piece __instance)
    {
        UndoHistory.OnCreatorSet(__instance);
    }
}

/// <summary>
/// Notes the piece the hammer removes, so removing it can be undone. Player.RemovePiece finds the piece
/// itself, and passes it to CheckCanRemovePiece before removing it.
/// </summary>
[HarmonyPatch(typeof(Player), "RemovePiece")]
internal static class Player_RemovePiece_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance)
    {
        UndoHistory.BeforeRemovePiece(__instance);
    }

    [HarmonyFinalizer]
    private static System.Exception Finalizer(System.Exception __exception, bool __result)
    {
        UndoHistory.AfterRemovePiece(__exception == null && __result);
        return __exception;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.CheckCanRemovePiece))]
internal static class Player_CheckCanRemovePiece_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Piece piece, bool __result)
    {
        if (__result)
        {
            UndoHistory.OnRemoveChecked(piece);
        }
    }
}

/// <summary>
/// Notes when a piece in the history is destroyed, by anyone, so undo knows it's gone rather than too
/// far away to reach.
/// </summary>
[HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
internal static class ZDOMan_HandleDestroyedZDO_Patch
{
    [HarmonyPrefix]
    private static void Prefix(ZDOID uid)
    {
        UndoHistory.OnDestroyed(uid);
    }
}

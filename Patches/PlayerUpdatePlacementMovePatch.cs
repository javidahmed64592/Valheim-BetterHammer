using HarmonyLib;

namespace NoHammerBuildMenu.Patches;

/// <summary>
/// Handles the move input (Ctrl+middle-click to pick up, left-click to put down, right-click to cancel)
/// and the copy-selection input (Alt+middle/right/left-click, then left-click to build) before vanilla's
/// placement input, and stops vanilla from also removing or placing the piece.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdatePlacement")]
internal static class Player_UpdatePlacement_Move_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance, bool takeInput, out bool __state)
    {
        __state = false;
        if (__instance == Player.m_localPlayer)
        {
            bool moving = PieceMover.BeforeUpdatePlacement(__instance, takeInput);
            bool copying = MultiCopy.BeforeUpdatePlacement(__instance, takeInput);
            __state = moving || copying;
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Player __instance, bool __state)
    {
        if (__state)
        {
            PieceMover.BlockVanillaPlacing(__instance);
        }
    }
}

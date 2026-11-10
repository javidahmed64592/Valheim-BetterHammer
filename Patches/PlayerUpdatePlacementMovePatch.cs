using HarmonyLib;

namespace NoHammerBuildMenu.Patches;

/// <summary>
/// Handles the move input (Ctrl+middle-click to pick up, left-click to put down, right-click to cancel)
/// before vanilla's placement input, and stops vanilla from also removing or placing the piece.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdatePlacement")]
internal static class Player_UpdatePlacement_Move_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance, bool takeInput, out bool __state)
    {
        __state = __instance == Player.m_localPlayer && PieceMover.BeforeUpdatePlacement(__instance, takeInput);
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

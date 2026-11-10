using HarmonyLib;

namespace NoHammerBuildMenu.Patches;

/// <summary>
/// Prevents Shift+RMB / Ctrl+RMB from also opening the build menu while we use them
/// for the repair and grid toggles, with the phantom hammer or any real hammer. The build
/// menu also stays closed while moving a piece, where right-click cancels the move.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdateBuildGuiInput")]
internal static class Player_UpdateBuildGuiInput_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Player __instance)
    {
        if (__instance != Player.m_localPlayer)
        {
            return true;
        }

        bool modifierHeld = NoHammerBuildMenuPlugin.ShiftHeld() || NoHammerBuildMenuPlugin.CtrlHeld();
        return !PieceMover.RightClickConsumed
            && (!modifierHeld || !NoHammerBuildMenuPlugin.IsHammerEquipped(__instance));
    }
}

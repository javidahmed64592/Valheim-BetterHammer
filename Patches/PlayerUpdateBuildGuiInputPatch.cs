using HarmonyLib;

namespace NoHammerBuildMenu.Patches;

/// <summary>
/// Prevents Shift+RMB / Ctrl+RMB / Alt+RMB from also opening the build menu while we use them
/// for the repair and grid toggles and deselecting, with the phantom hammer or any real hammer.
/// The build menu also stays closed while moving a piece or placing a copy, where right-click
/// cancels.
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

        bool modifierHeld = NoHammerBuildMenuPlugin.ShiftHeld() || NoHammerBuildMenuPlugin.CtrlHeld() || NoHammerBuildMenuPlugin.AltHeld();
        return !PieceMover.RightClickConsumed
            && !MultiCopy.RightClickConsumed
            && (!modifierHeld || !NoHammerBuildMenuPlugin.IsHammerEquipped(__instance));
    }
}

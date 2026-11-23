using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Tabs (such as the build menu's) switch with Q and E. The game stops that while one of its popups is
/// open, but not while its text box is, so typing Q or E when naming a copy switched tabs and took the
/// focus away from the text box.
/// </summary>
[HarmonyPatch(typeof(TabHandler), "Update")]
internal static class TabHandler_Update_Patch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !TextInput.IsVisible();
    }
}

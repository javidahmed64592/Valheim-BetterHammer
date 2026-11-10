using System.Reflection;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Ctrl+scroll adjusts the grid, so stop vanilla from also using it to rotate the piece.
/// </summary>
[HarmonyPatch]
internal static class ZInput_GetMouseScrollWheel_Patch
{
    private static MethodInfo TargetMethod()
    {
        return AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel");
    }

    private static bool Prepare()
    {
        return TargetMethod() != null;
    }

    [HarmonyPostfix]
    private static void Postfix(ref float __result)
    {
        Player player = Player.m_localPlayer;
        if (player != null && BetterHammerPlugin.CtrlHeld() && BetterHammerPlugin.GridActive(player))
        {
            __result = 0f;
        }
    }
}

using System.Reflection;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Stops Alt+scroll from also rotating the piece: UpdatePlacement accumulates scroll into
/// the rotation, so restore the rotation state it had before the call.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdatePlacement")]
internal static class Player_UpdatePlacement_Patch
{
    private static readonly FieldInfo ScrollField = AccessTools.Field(typeof(Player), "m_scrollCurrAmount");
    private static readonly FieldInfo RotationField = AccessTools.Field(typeof(Player), "m_placeRotation");

    [HarmonyPrefix]
    private static void Prefix(Player __instance, out (float scroll, int rotation)? __state)
    {
        __state = null;
        if (ScrollField != null && RotationField != null && BetterHammerPlugin.AltZoomActive(__instance))
        {
            __state = ((float)ScrollField.GetValue(__instance), (int)RotationField.GetValue(__instance));
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Player __instance, (float scroll, int rotation)? __state)
    {
        if (__state == null)
        {
            return;
        }

        ScrollField.SetValue(__instance, __state.Value.scroll);
        RotationField.SetValue(__instance, __state.Value.rotation);
    }
}

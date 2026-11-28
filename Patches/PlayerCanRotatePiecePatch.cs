using BetterHammer.Core;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Vanilla only zooms the camera while placing if the piece can't rotate (it uses scroll to
/// rotate otherwise). Report "can't rotate" while Alt is held so the camera zooms instead.
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.CanRotatePiece))]
[Feature(Feature)]
internal static class Player_CanRotatePiece_Patch
{
    /// <summary>Alt+scroll zooms while building. Without it, Alt+scroll rotates the piece, as in vanilla.</summary>
    public const string Feature = "Zoom camera";

    [HarmonyPostfix]
    private static void Postfix(Player __instance, ref bool __result)
    {
        if (__result && BetterHammerPlugin.AltZoomActive(__instance))
        {
            __result = false;
        }
    }
}

using BetterHammer.Structures;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Shift+Middle-Click copies a piece's PaintBrush paint along with its type (see <see cref="PaintCopy"/>).
/// </summary>
[HarmonyPatch(typeof(Player), "CopyPiece")]
internal static class Player_CopyPiece_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance)
    {
        PaintCopy.BeforeCopyPiece(__instance);
    }

    [HarmonyFinalizer]
    private static System.Exception Finalizer(System.Exception __exception, bool __result)
    {
        PaintCopy.AfterCopyPiece(__exception == null && __result);
        return __exception;
    }
}

/// <summary>
/// Choosing a piece in the build menu drops a copied paint, even when it's the same piece.
/// </summary>
[HarmonyPatch(typeof(BuildUi), nameof(BuildUi.OnSelectPiece))]
internal static class BuildUi_OnSelectPiece_Patch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PaintCopy.ClearCopied();
    }
}

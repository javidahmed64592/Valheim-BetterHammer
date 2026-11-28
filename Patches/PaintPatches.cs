using BetterHammer.Core;
using BetterHammer.Structures;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// Shift+Middle-Click copies a piece's PaintBrush paint along with its type (see <see cref="PaintCopy"/>).
/// </summary>
[HarmonyPatch(typeof(Player), "CopyPiece")]
[Feature(Feature)]
internal static class Player_CopyPiece_Patch
{
    /// <summary>
    /// Shift+Middle-Click also copies PaintBrush paint. Without it, it copies the piece type only, as in vanilla.
    /// </summary>
    public const string Feature = "Copy paint with piece";

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
[Feature(Player_CopyPiece_Patch.Feature)]
internal static class BuildUi_OnSelectPiece_Patch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PaintCopy.ClearCopied();
    }
}

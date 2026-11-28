using BetterHammer.Core;

using HarmonyLib;

namespace BetterHammer.Patches;

/// <summary>
/// In debug mode the game reads Z, B, K and L on their own (fly, no build cost, kill enemies, remove drops), whatever
/// else is held. On a frame where one of this mod's combos goes down, such as Ctrl+Z to undo, debug mode is off while
/// the player updates, so the combo doesn't also toggle flying. A key on its own still does both, as before.
/// </summary>
[HarmonyPatch(typeof(Player), "Update")]
[Feature(Feature)]
internal static class Player_Update_DebugKeys_Patch
{
    /// <summary>Without it, a combo such as Ctrl+Z also does what its key does in debug mode.</summary>
    public const string Feature = "Combos skip debug keys";

    private static void Prefix(Player __instance, out bool __state)
    {
        // Only the mod that turned debug mode off turns it back on, so several mods doing this don't clash.
        __state = Player.m_debugMode && __instance == Player.m_localPlayer && KeyBinding.AnyComboDown();
        if (__state)
        {
            Player.m_debugMode = false;
        }
    }

    private static void Finalizer(bool __state)
    {
        if (__state)
        {
            Player.m_debugMode = true;
        }
    }
}

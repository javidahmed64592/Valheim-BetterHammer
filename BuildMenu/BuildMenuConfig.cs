using BepInEx.Configuration;

namespace BetterHammer.BuildMenu;

internal static class BuildMenuConfig
{
    private static ConfigEntry<bool> _sections;

    internal static bool Sections => _sections == null || _sections.Value;

    internal static void Bind(ConfigFile config)
    {
        _sections = config.Bind(
            "Build Menu",
            "Sections",
            true,
            "Split the build menu's pieces into sections with headers, which you can collapse by clicking them. " +
            "Turn this off to use the game's own menu. Takes effect after a restart.");
    }
}

using BepInEx.Configuration;

namespace BetterHammer.BuildMenu;

internal static class BuildMenuConfig
{
    private static ConfigEntry<bool> _sections;
    private static ConfigEntry<int> _iconSize;

    internal static bool Sections => _sections == null || _sections.Value;

    /// <summary>
    /// How big the piece buttons are, relative to the game's.
    /// </summary>
    internal static float IconScale => (_iconSize?.Value ?? 100) / 100f;

    internal static void Bind(ConfigFile config)
    {
        _sections = config.Bind(
            "Build Menu",
            "Sections",
            true,
            "Split the build menu's pieces into sections with headers, which you can collapse by clicking them. " +
            "Turn this off to use the game's own menu. Takes effect after a restart.");

        _iconSize = config.Bind(
            "Build Menu",
            "Icon Size",
            100,
            new ConfigDescription(
                "Size of the piece buttons in the sectioned build menu, as a percentage of the game's. Smaller fits " +
                "more pieces in each row. Takes effect after a restart.",
                new AcceptableValueRange<int>(60, 140)));
    }
}

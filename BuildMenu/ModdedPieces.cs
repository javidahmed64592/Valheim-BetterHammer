using System.Collections.Generic;

using Jotunn.Utils;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Which mod added a piece, using Jotunn's record of the prefabs every mod adds (Jotunn or not).
/// </summary>
internal static class ModdedPieces
{
    private static readonly Dictionary<string, string> SourceByPrefab = [];

    /// <summary>
    /// Turns on Jotunn's record. It has to start before the game's main menu loads.
    /// </summary>
    internal static void Enable()
    {
        if (BuildMenuConfig.Sections)
        {
            ModQuery.Enable();
        }
    }

    /// <summary>
    /// The name of the mod that added the piece, or null for the game's own pieces.
    /// </summary>
    internal static string SourceOf(Piece piece)
    {
        if (piece == null || ZNetScene.instance == null)
        {
            return null;
        }

        string prefab = piece.gameObject.name;
        if (!SourceByPrefab.TryGetValue(prefab, out string source))
        {
            source = ModQuery.GetPrefab(prefab)?.SourceMod?.Name;
            SourceByPrefab[prefab] = source;
        }

        return source;
    }

    internal static bool IsModded(Piece piece)
    {
        return SourceOf(piece) != null;
    }
}

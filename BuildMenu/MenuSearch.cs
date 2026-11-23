namespace BetterHammer.BuildMenu;

/// <summary>
/// Search prefixes in the build menu's search box: <c>@</c> for the mod a piece comes from, <c>#</c> for
/// the section it's in, and <c>$</c> for a material it needs. Without a prefix the game's search is used
/// (the piece's name and materials).
/// </summary>
internal static class MenuSearch
{
    private const char ModPrefix = '@';
    private const char SectionPrefix = '#';
    private const char MaterialPrefix = '$';

    /// <summary>
    /// Whether the search uses a prefix.
    /// </summary>
    /// <param name="term">The search box's text, lower case without spaces, as the game searches it.</param>
    internal static bool HasPrefix(string term)
    {
        return term.Length > 1 && term[0] is ModPrefix or SectionPrefix or MaterialPrefix;
    }

    internal static bool Matches(Piece piece, string sectionTitle, string term)
    {
        string text = term.Substring(1);
        return term[0] switch
        {
            ModPrefix => Contains(ModdedPieces.SourceOf(piece), text),
            SectionPrefix => Contains(sectionTitle, text),
            _ => NeedsMaterial(piece, text),
        };
    }

    private static bool NeedsMaterial(Piece piece, string text)
    {
        foreach (Piece.Requirement requirement in piece.m_resources ?? [])
        {
            if (requirement?.m_resItem != null
                && Contains(Localization.instance.Localize(requirement.m_resItem.m_itemData.m_shared.m_name), text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string value, string text)
    {
        return value != null && value.Replace(" ", "").ToLowerInvariant().Contains(text);
    }
}

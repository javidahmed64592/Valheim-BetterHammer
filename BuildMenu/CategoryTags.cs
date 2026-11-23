using System.Collections.Generic;
using System.Linq;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Changes to the Categories tab's list of categories and what each one shows, so they match the
/// sections in Show All.
/// </summary>
internal static class CategoryTags
{
    /// <summary>
    /// After the game (and Jotunn) listed the categories: drops the game's categories that no piece ends
    /// up in, adds Modded when there are modded pieces, and puts them in group order.
    /// </summary>
    internal static void Update(ByUsagePieceList list, PieceTable table)
    {
        if (table == null)
        {
            return;
        }

        PieceCategories.Refresh(list, table);
        List<int> tags = list.m_availableTags;
        _ = tags.RemoveAll(id => id == PieceCategories.ModdedTagId
            || (PieceCategories.IsSortedTag(list, id) && !PieceCategories.HasPieces(list, table, id)));

        if (table.m_availablePieces.Any(piece => piece != null && !PieceCategories.IsSpecial(piece) && ModdedPieces.IsModded(piece)))
        {
            tags.Add(PieceCategories.ModdedTagId);
        }

        List<int> ordered = [.. tags.OrderBy(id => PieceCategories.Find(list, table, id)?.Order ?? 1500)];
        tags.Clear();
        tags.AddRange(ordered);
    }

    /// <summary>
    /// The Modded category: the repair and remove tools, then every modded piece, in the game's order.
    /// </summary>
    internal static void AddModdedPieces(PieceTable table, IList<Piece> result)
    {
        foreach (Piece piece in table.m_availablePieces)
        {
            if (piece != null && (PieceCategories.IsSpecial(piece) || ModdedPieces.IsModded(piece)))
            {
                result.Add(piece);
            }
        }
    }

    /// <summary>
    /// One of the game's categories: exactly the pieces in it (for Building, the pieces in every
    /// construction category), rather than every piece tagged with it. Keeps the repair and remove tools
    /// the game adds to every category.
    /// </summary>
    internal static void Filter(ByUsagePieceList list, int tagId, PieceTable table, IList<Piece> result)
    {
        result.Clear();
        foreach (Piece piece in table.m_availablePieces)
        {
            if (piece != null && (PieceCategories.IsSpecial(piece) || PieceCategories.Shows(list, table, tagId, piece)))
            {
                result.Add(piece);
            }
        }
    }
}

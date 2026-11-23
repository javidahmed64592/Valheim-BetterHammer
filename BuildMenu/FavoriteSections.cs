using System.Collections.Generic;
using System.Linq;

namespace BetterHammer.BuildMenu;

/// <summary>
/// The Favorites tab: a section for each of the player's favourites categories, plus Uncategorised, with
/// the pieces in the order the player arranged them. Categories can be renamed (right-click) and
/// reordered, and pieces dragged between sections (see <see cref="FavoriteDrag"/>). The categories and
/// which pieces are in them stay in the game's character save; only the arranged order is kept by
/// BetterHammer, in <c>build_menu.json</c>, so removing the mod leaves the favourites as they were.
/// </summary>
internal static class FavoriteSections
{
    internal const int Uncategorised = -1;
    private const string UncategorisedKey = "";

    /// <summary>
    /// Show All: a section per category, in the player's order, then Uncategorised. A piece in two
    /// categories shows in both. A single category: its pieces on their own.
    /// </summary>
    internal static List<SectionSpec> Group(FavoritePieceList list, int tagId, IReadOnlyList<Piece> pieces)
    {
        List<SectionSpec> sections = [];
        if (tagId >= 0 && tagId < list.TagCount)
        {
            sections.Add(Section(list, tagId, pieces, keepEmpty: false));
            return sections;
        }

        for (int tag = 0; tag < list.TagCount; tag++)
        {
            sections.Add(Section(list, tag, pieces, keepEmpty: true));
        }

        SectionSpec uncategorised = Section(list, Uncategorised, pieces, keepEmpty: list.TagCount > 0);
        if (uncategorised.Pieces.Count > 0 || list.TagCount > 0)
        {
            sections.Add(uncategorised);
        }

        return sections;
    }

    private static SectionSpec Section(FavoritePieceList list, int tag, IReadOnlyList<Piece> pieces, bool keepEmpty)
    {
        string name = NameOf(list, tag);
        SectionSpec section = new(
            "favorites/" + (tag == Uncategorised ? "uncategorised" : name),
            tag == Uncategorised ? Localization.instance.Localize("$betterhammer_favorites_uncategorised") : name,
            tag == Uncategorised ? int.MaxValue : tag)
        {
            FavoriteTag = tag,
            KeepEmpty = keepEmpty,
        };

        IEnumerable<Piece> members = pieces.Where(piece => !PieceCategories.IsSpecial(piece) && IsIn(list, piece, tag));
        section.Pieces.AddRange(Arranged(members, MenuState.FavoriteOrderOf(OrderKey(list, tag))));
        return section;
    }

    /// <summary>
    /// Pieces the player arranged come first, in that order; the rest follow in the game's order.
    /// </summary>
    private static IEnumerable<Piece> Arranged(IEnumerable<Piece> pieces, IReadOnlyList<string> order)
    {
        Dictionary<string, int> position = [];
        for (int i = 0; i < order.Count; i++)
        {
            position[order[i]] = i;
        }

        return pieces.Select((piece, index) => (piece, index))
            .OrderBy(entry => position.TryGetValue(entry.piece.gameObject.name, out int at) ? at : order.Count + entry.index)
            .Select(entry => entry.piece);
    }

    private static bool IsIn(FavoritePieceList list, Piece piece, int tag)
    {
        if (!list.IsFavorite(piece))
        {
            return false;
        }

        HashSet<int> tags = list.GetTags(piece.gameObject.name);
        return tag == Uncategorised ? tags.Count == 0 : tags.Contains(tag);
    }

    private static string NameOf(FavoritePieceList list, int tag)
    {
        return tag >= 0 && tag < list.TagCount ? list.GetTagDisplayName(tag) : UncategorisedKey;
    }

    private static string OrderKey(FavoritePieceList list, int tag)
    {
        return tag == Uncategorised ? UncategorisedKey : NameOf(list, tag);
    }

    /// <summary>
    /// Asks for a new name for a category, with the game's rules for names.
    /// </summary>
    internal static void OpenRename(BuildUi ui, int tag)
    {
        FavoritePieceList list = ui.m_favoritePieceList;
        if (tag < 0 || tag >= list.TagCount)
        {
            return;
        }

        string current = list.GetTagDisplayName(tag);
        UnifiedPopup.Push(new TextEntryPopup(
            Localization.instance.Localize("$betterhammer_favorites_rename_header"),
            Localization.instance.Localize("$betterhammer_favorites_rename_text", current),
            current,
            UnifiedPopup.Pop,
            FavoritePieceList.ValidateTagName,
            name =>
            {
                if (!FavoritePieceList.ValidateTagName(name))
                {
                    return;
                }

                UnifiedPopup.Pop();
                Rename(ui, tag, name.Trim());
            },
            localizeText: false));
        UnifiedPopup.SetFocus();
    }

    private static void Rename(BuildUi ui, int tag, string name)
    {
        FavoritePieceList list = ui.m_favoritePieceList;
        string old = list.m_tags[tag];
        if (old == name)
        {
            return;
        }

        list.m_tags[tag] = name;
        MenuState.RenameFavoriteOrder(old, name);
        ui.OnFavoriteTagsChanged();
    }

    /// <summary>
    /// Moves a category to another place in the list. Pieces keep their categories: the game stores
    /// them by place, so every piece's set is renumbered to match.
    /// </summary>
    internal static void MoveCategory(BuildUi ui, int from, int to)
    {
        FavoritePieceList list = ui.m_favoritePieceList;
        int count = list.m_tags.Count;
        if (from < 0 || from >= count || to < 0 || to >= count || from == to)
        {
            return;
        }

        List<int> order = [.. Enumerable.Range(0, count)];
        order.RemoveAt(from);
        order.Insert(to, from);

        // order[newPlace] = oldPlace; invert it to renumber.
        int[] newPlaceOf = new int[count];
        for (int place = 0; place < count; place++)
        {
            newPlaceOf[order[place]] = place;
        }

        List<string> names = [.. order.Select(old => list.m_tags[old])];
        list.m_tags.Clear();
        list.m_tags.AddRange(names);

        foreach (string piece in list.m_favoriteBuildPieces.Keys.ToList())
        {
            list.m_favoriteBuildPieces[piece] = [.. list.m_favoriteBuildPieces[piece].Where(tag => tag >= 0 && tag < count).Select(tag => newPlaceOf[tag])];
        }

        ui.OnFavoriteTagsChanged();
    }

    /// <summary>
    /// Moves a piece from one section to another, or to another place in the same one. Moving it to a
    /// category takes it out of the one it came from (it stays in any others); moving it to Uncategorised
    /// takes it out of every category, since that section shows pieces in none. Saves the new order of
    /// the section it joined.
    /// </summary>
    /// <param name="ui">The build menu.</param>
    /// <param name="piece">The dragged piece.</param>
    /// <param name="from">The section it was dragged from.</param>
    /// <param name="to">The section it was dropped on.</param>
    /// <param name="toPieces">The pieces of that section, in order.</param>
    /// <param name="before">The piece it was dropped on, to go before; null for the end.</param>
    internal static void MovePiece(BuildUi ui, Piece piece, int from, int to, IReadOnlyList<Piece> toPieces, Piece before)
    {
        FavoritePieceList list = ui.m_favoritePieceList;
        string name = piece.gameObject.name;
        if (!list.IsFavorite(name))
        {
            return;
        }

        HashSet<int> tags = list.GetTags(name);
        if (from != to)
        {
            if (from >= 0)
            {
                _ = tags.Remove(from);
            }

            if (to >= 0)
            {
                _ = tags.Add(to);
            }
            else
            {
                // Uncategorised means in no category at all.
                tags.Clear();
            }
        }

        List<string> order = [.. toPieces.Where(other => other != piece).Select(other => other.gameObject.name)];
        int at = before != null && before != piece ? order.IndexOf(before.gameObject.name) : -1;
        order.Insert(at >= 0 ? at : order.Count, name);
        MenuState.SetFavoriteOrder(OrderKey(list, to), order);
    }

    /// <summary>
    /// Whether the Favorites tab is showing its sections as the player arranged them, so pieces and
    /// categories can be dragged.
    /// </summary>
    internal static bool CanArrange(BuildUi ui)
    {
        IPieceList list = ui.m_pieceLists[ui.m_currentPieceList];
        string tab = MenuSections.TabKey(list);
        return list is FavoritePieceList
            && MenuState.GroupOf(tab) == GroupMode.Type
            && MenuState.SortOf(tab) == SortMode.Default
            && !ui.m_currentBuildTool.m_hideAdvancedMenu;
    }
}

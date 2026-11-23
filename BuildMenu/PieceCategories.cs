using System;
using System.Collections.Generic;

namespace BetterHammer.BuildMenu;

/// <summary>
/// One entry of the Categories tab: a game usage tag, a category another mod adds (such as the Copies
/// tab), or BetterHammer's Modded category.
/// </summary>
internal sealed class CategoryInfo
{
    internal CategoryInfo(int tagId, Piece.UsageTagFlags flag, string title, string key, int order, CategoryGroup group)
    {
        TagId = tagId;
        Flag = flag;
        Title = title;
        Key = key;
        Order = order;
        Group = group;
    }

    /// <summary>
    /// The id the game's Categories tab uses for it.
    /// </summary>
    internal int TagId { get; }

    /// <summary>
    /// The usage tag, or 0 for a category another mod adds.
    /// </summary>
    internal Piece.UsageTagFlags Flag { get; }

    internal string Title { get; }

    /// <summary>
    /// Stable name for remembering collapsed sections.
    /// </summary>
    internal string Key { get; }

    /// <summary>
    /// Where it's listed: on the left and as a section in Show All.
    /// </summary>
    internal int Order { get; }

    internal CategoryGroup Group { get; }
}

internal enum CategoryGroup
{
    Construction = 0,
    Utility = 1,
    Comfort = 2,
    Food = 3,
    Mods = 4,
}

/// <summary>
/// Puts every piece of the Categories tab in exactly one category, so a piece shows under the same
/// category in Show All and when its category is picked on the left. The game tags most pieces several
/// times; the most specific tag wins, a move in <see cref="SectionRules"/> overrides it, and a category
/// another mod put the piece in beats them all. Building is the game's tag for every construction piece,
/// so it only wins when a piece has no other tag; picking Building shows every piece in a construction
/// category.
/// </summary>
internal static class PieceCategories
{
    /// <summary>
    /// The tag id of BetterHammer's Modded category, well clear of the game's tags and the ids Jotunn
    /// gives other mods' categories (which count up from the game's tags).
    /// </summary>
    internal const int ModdedTagId = 1_000_000;

    /// <summary>
    /// The tag id of BetterHammer's Hidden category, for the pieces the player hid.
    /// </summary>
    internal const int HiddenTagId = 1_000_001;

    /// <summary>
    /// The order categories are listed in, in groups: construction, utility, comfort and food.
    /// </summary>
    private static readonly (Piece.UsageTagFlags Flag, CategoryGroup Group)[] DisplayOrder =
    [
        (Piece.UsageTagFlags.Building, CategoryGroup.Construction),
        (Piece.UsageTagFlags.Floor, CategoryGroup.Construction),
        (Piece.UsageTagFlags.Wall, CategoryGroup.Construction),
        (Piece.UsageTagFlags.Roof, CategoryGroup.Construction),
        (Piece.UsageTagFlags.Architecture, CategoryGroup.Construction),
        (Piece.UsageTagFlags.Stairs, CategoryGroup.Construction),
        (Piece.UsageTagFlags.Doors, CategoryGroup.Construction),
        (Piece.UsageTagFlags.Crafting, CategoryGroup.Utility),
        (Piece.UsageTagFlags.Storage, CategoryGroup.Utility),
        (Piece.UsageTagFlags.Stacks, CategoryGroup.Utility),
        (Piece.UsageTagFlags.Transport, CategoryGroup.Utility),
        (Piece.UsageTagFlags.Defense, CategoryGroup.Utility),
        (Piece.UsageTagFlags.Misc, CategoryGroup.Utility),
        (Piece.UsageTagFlags.Furniture, CategoryGroup.Comfort),
        (Piece.UsageTagFlags.Lighting, CategoryGroup.Comfort),
        (Piece.UsageTagFlags.Decor, CategoryGroup.Comfort),
        (Piece.UsageTagFlags.Seasonal, CategoryGroup.Comfort),
        (Piece.UsageTagFlags.Food, CategoryGroup.Food),
        (Piece.UsageTagFlags.Meads, CategoryGroup.Food),
        (Piece.UsageTagFlags.Feasts, CategoryGroup.Food),
    ];

    /// <summary>
    /// Most specific first: a piece goes under the first of its tags in this list.
    /// </summary>
    private static readonly Piece.UsageTagFlags[] Priority =
    [
        Piece.UsageTagFlags.Stairs,
        Piece.UsageTagFlags.Doors,
        Piece.UsageTagFlags.Floor,
        Piece.UsageTagFlags.Wall,
        Piece.UsageTagFlags.Roof,
        Piece.UsageTagFlags.Architecture,
        Piece.UsageTagFlags.Lighting,
        Piece.UsageTagFlags.Storage,
        Piece.UsageTagFlags.Crafting,
        Piece.UsageTagFlags.Furniture,
        Piece.UsageTagFlags.Transport,
        Piece.UsageTagFlags.Defense,
        Piece.UsageTagFlags.Seasonal,
        Piece.UsageTagFlags.Decor,
        Piece.UsageTagFlags.Feasts,
        Piece.UsageTagFlags.Meads,
        Piece.UsageTagFlags.Food,
        Piece.UsageTagFlags.Stacks,
        Piece.UsageTagFlags.Building,
        Piece.UsageTagFlags.Misc,
    ];

    private static readonly Dictionary<Piece, CategoryInfo> Primary = [];
    private static readonly Dictionary<int, CategoryInfo> ById = [];
    private static readonly List<Piece> Temp = [];
    private static ByUsagePieceList _list;
    private static PieceTable _table;
    private static int _pieceCount = -1;

    internal static CategoryInfo Hidden => new(
        HiddenTagId, 0, Localization.instance.Localize("$betterhammer_menu_hidden"), "hidden", 2001, CategoryGroup.Mods);

    /// <summary>
    /// True for BetterHammer's own categories (Modded, Hidden), which it fills itself.
    /// </summary>
    internal static bool IsOwnTag(int tagId)
    {
        return tagId is ModdedTagId or HiddenTagId;
    }

    internal static CategoryInfo Modded => new(
        ModdedTagId, 0, Localization.instance.Localize("$betterhammer_menu_modded"), "modded", 2000, CategoryGroup.Mods);

    /// <summary>
    /// True when the tag is one of the game's own usage tags, which show the pieces in that category
    /// (see <see cref="Shows"/>) rather than every piece tagged with it.
    /// </summary>
    internal static bool IsSortedTag(ByUsagePieceList list, int tagId)
    {
        return tagId >= 0 && tagId < list.m_usageTags.Length;
    }

    /// <summary>
    /// Whether picking the category shows the piece: the pieces in it, or for Building, every piece in
    /// a construction category.
    /// </summary>
    internal static bool Shows(ByUsagePieceList list, PieceTable table, int tagId, Piece piece)
    {
        bool building = tagId < list.m_usageTags.Length && list.m_usageTags[tagId] == Piece.UsageTagFlags.Building;
        return Of(list, table, piece) switch
        {
            null => false,
            CategoryInfo category when building => category.Group == CategoryGroup.Construction,
            CategoryInfo category => category.TagId == tagId,
        };
    }

    /// <summary>
    /// Sorts every available piece into its category. Called when the game updates the tab's tags, which
    /// it does before showing pieces; also called lazily if the pieces changed since.
    /// </summary>
    internal static void Refresh(ByUsagePieceList list, PieceTable table)
    {
        _list = list;
        _table = table;
        _pieceCount = table != null ? table.m_availablePieces.Count : -1;
        Primary.Clear();
        ById.Clear();
        if (table == null)
        {
            return;
        }

        Piece.UsageTagFlags available = 0;
        foreach (Piece piece in table.m_availablePieces)
        {
            available |= piece.m_usage;
        }

        for (int id = 0; id < list.m_usageTags.Length; id++)
        {
            Piece.UsageTagFlags flag = list.m_usageTags[id];
            int order = Array.FindIndex(DisplayOrder, entry => entry.Flag == flag);
            ById[id] = new CategoryInfo(
                id,
                flag,
                Localization.instance.Localize(list.m_usageTagDisplayNames[id]),
                flag.ToString(),
                order >= 0 ? order : 500 + id,
                order >= 0 ? DisplayOrder[order].Group : CategoryGroup.Utility);
        }

        // Categories other mods add, in the order they were added. Their pieces stay in them.
        for (int i = 0; i < list.m_availableTags.Count; i++)
        {
            int id = list.m_availableTags[i];
            if (id < list.m_usageTags.Length || IsOwnTag(id))
            {
                continue;
            }

            string token = list.GetTagDisplayName(i);
            CategoryInfo custom = new(id, 0, Localization.instance.Localize(token), "category:" + token, 1000 + i, CategoryGroup.Mods);
            ById[id] = custom;
            Temp.Clear();
            list.GetAvailablePiecesWithTag(id, table, Temp);
            foreach (Piece piece in Temp)
            {
                if (piece != null && !IsSpecial(piece))
                {
                    Primary[piece] = custom;
                }
            }
        }

        Temp.Clear();
        foreach (Piece piece in table.m_availablePieces)
        {
            if (piece == null || IsSpecial(piece) || Primary.ContainsKey(piece))
            {
                continue;
            }

            Piece.UsageTagFlags flag = MainTag(piece, available);
            int id = Array.IndexOf(list.m_usageTags, flag);
            if (id >= 0)
            {
                Primary[piece] = ById[id];
            }
        }
    }

    /// <summary>
    /// The category a piece is in, or null if it has no tags.
    /// </summary>
    internal static CategoryInfo Of(ByUsagePieceList list, PieceTable table, Piece piece)
    {
        EnsureFresh(list, table);
        return Primary.TryGetValue(piece, out CategoryInfo category) ? category : null;
    }

    /// <summary>
    /// A category by the id the game's tab uses for it.
    /// </summary>
    internal static CategoryInfo Find(ByUsagePieceList list, PieceTable table, int tagId)
    {
        if (tagId == ModdedTagId)
        {
            return Modded;
        }

        if (tagId == HiddenTagId)
        {
            return Hidden;
        }

        EnsureFresh(list, table);
        return ById.TryGetValue(tagId, out CategoryInfo category) ? category : null;
    }

    /// <summary>
    /// Whether picking the category would show any piece.
    /// </summary>
    internal static bool HasPieces(ByUsagePieceList list, PieceTable table, int tagId)
    {
        EnsureFresh(list, table);
        foreach (Piece piece in Primary.Keys)
        {
            if (Shows(list, table, tagId, piece))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsSpecial(Piece piece)
    {
        return piece.m_repairPiece || piece.m_removePiece;
    }

    private static void EnsureFresh(ByUsagePieceList list, PieceTable table)
    {
        if (list != _list || table != _table || table == null || table.m_availablePieces.Count != _pieceCount)
        {
            Refresh(list, table);
        }
    }

    private static Piece.UsageTagFlags MainTag(Piece piece, Piece.UsageTagFlags available)
    {
        if (SectionRules.TryGetMove(piece.gameObject.name, out Piece.UsageTagFlags moved) && (available & moved) != 0)
        {
            return moved;
        }

        foreach (Piece.UsageTagFlags flag in Priority)
        {
            if ((piece.m_usage & flag) != 0)
            {
                return flag;
            }
        }

        // A tag this list doesn't know (a newer game version): the lowest one set.
        int usage = (int)piece.m_usage;
        return (Piece.UsageTagFlags)(usage & -usage);
    }
}

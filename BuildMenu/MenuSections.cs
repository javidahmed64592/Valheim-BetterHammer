using System;
using System.Collections.Generic;

namespace BetterHammer.BuildMenu;

/// <summary>
/// One header and grid in the build menu: the pieces under it, in the order they're shown.
/// </summary>
internal sealed class SectionSpec
{
    internal SectionSpec(string key, string title, int order)
    {
        Key = key;
        Title = title;
        Order = order;
    }

    /// <summary>
    /// Identifies the section within its tab, e.g. for remembering that it's collapsed.
    /// </summary>
    internal string Key { get; }

    internal string Title { get; }

    internal int Order { get; }

    internal List<Piece> Pieces { get; } = [];
}

/// <summary>
/// Splits the pieces a build menu tab shows into sections. Categories → Show All gets one section per
/// usage tag, with each piece under its most specific tag. Categories that other mods add (such as the
/// Copies tab) come after the game's own. Everything else is one section without a header, as in the
/// game.
/// </summary>
internal static class MenuSections
{
    /// <summary>
    /// The order of the sections in Show All, grouped as construction, utility, comfort and food.
    /// </summary>
    private static readonly Piece.UsageTagFlags[] DisplayOrder =
    [
        Piece.UsageTagFlags.Building,
        Piece.UsageTagFlags.Floor,
        Piece.UsageTagFlags.Wall,
        Piece.UsageTagFlags.Roof,
        Piece.UsageTagFlags.Architecture,
        Piece.UsageTagFlags.Stairs,
        Piece.UsageTagFlags.Doors,
        Piece.UsageTagFlags.Crafting,
        Piece.UsageTagFlags.Storage,
        Piece.UsageTagFlags.Transport,
        Piece.UsageTagFlags.Defense,
        Piece.UsageTagFlags.Misc,
        Piece.UsageTagFlags.Furniture,
        Piece.UsageTagFlags.Lighting,
        Piece.UsageTagFlags.Decor,
        Piece.UsageTagFlags.Seasonal,
        Piece.UsageTagFlags.Food,
        Piece.UsageTagFlags.Meads,
        Piece.UsageTagFlags.Feasts,
        Piece.UsageTagFlags.Stacks,
    ];

    /// <summary>
    /// Most specific first. A piece goes under the first of its tags in this list: a wood wall is tagged
    /// both Building and Wall, so it goes under Wall. Building is the game's tag for every construction
    /// piece, so it comes last, as does Misc: a dragon adornment tagged Building and Decor goes under Decor.
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

    /// <summary>
    /// Vanilla pieces whose tags put them in the wrong section, by prefab name. A piece only moves if the
    /// section exists in the tab.
    /// </summary>
    private static readonly Dictionary<string, Piece.UsageTagFlags> Overrides = new()
    {
        // The other inverted wood walls are tagged Wall; this one was missed.
        ["wood_wall_roof_upsidedown"] = Piece.UsageTagFlags.Wall,

        // Black marble: the solid blocks go with the tile walls (some are also tagged Floor), and the
        // shaped trim (plinths and their corners, overhangs, columns, the arch and the spire) goes
        // together in Architecture.
        ["blackmarble_1x1"] = Piece.UsageTagFlags.Wall,
        ["blackmarble_2x1x1"] = Piece.UsageTagFlags.Wall,
        ["blackmarble_2x2x1"] = Piece.UsageTagFlags.Wall,
        ["blackmarble_2x2x2"] = Piece.UsageTagFlags.Wall,
        ["blackmarble_base_1"] = Piece.UsageTagFlags.Architecture,
        ["blackmarble_base_2"] = Piece.UsageTagFlags.Architecture,
        ["blackmarble_basecorner"] = Piece.UsageTagFlags.Architecture,
        ["blackmarble_out_1"] = Piece.UsageTagFlags.Architecture,
        ["blackmarble_out_2"] = Piece.UsageTagFlags.Architecture,
        ["blackmarble_outcorner"] = Piece.UsageTagFlags.Architecture,
        ["blackmarble_tip"] = Piece.UsageTagFlags.Architecture,

        // Tagged Doors as well as Wall, but they're walls with a window cut out.
        ["Piece_grausten_window_2x2"] = Piece.UsageTagFlags.Wall,
        ["Piece_grausten_window_4x2"] = Piece.UsageTagFlags.Wall,

        // Gates that lift away, built to keep enemies out.
        ["piece_drawbridge"] = Piece.UsageTagFlags.Defense,
        ["piece_drawbridge_log"] = Piece.UsageTagFlags.Defense,
    };

    private const string OtherKey = "other";

    private static readonly List<Piece> TagPieces = [];

    /// <summary>
    /// The sections for a tab's current tag.
    /// </summary>
    /// <param name="list">The tab's piece list.</param>
    /// <param name="tagId">The selected tag, or -1 for Show All.</param>
    /// <param name="table">The build tool's piece table.</param>
    /// <param name="pieces">The pieces the game shows, in its order. Every one ends up in a section.</param>
    internal static List<SectionSpec> Build(IPieceList list, int tagId, PieceTable table, IReadOnlyList<Piece> pieces)
    {
        if (list is ByUsagePieceList usage && tagId == -1 && list.ShowTags && list.TagCount > 1)
        {
            return ByUsageTag(usage, table, pieces);
        }

        SectionSpec single = new("all", "", 0);
        single.Pieces.AddRange(pieces);
        return [single];
    }

    private static List<SectionSpec> ByUsageTag(ByUsagePieceList list, PieceTable table, IReadOnlyList<Piece> pieces)
    {
        Dictionary<Piece, (int Rank, SectionSpec Section)> placed = [];
        Dictionary<Piece.UsageTagFlags, SectionSpec> byFlag = [];
        List<SectionSpec> sections = [];
        int vanillaTags = list.m_usageTags.Length;

        for (int i = 0; i < list.TagCount; i++)
        {
            int id = list.GetTagIdByIndex(i);
            string title = Localization.instance.Localize(list.GetTagDisplayName(i));
            SectionSpec section;
            int rank;

            // Ids past the game's own tags are categories other mods add (Jotunn adds them this way).
            // They go first in priority, since a mod chose to put its pieces there.
            if (id >= vanillaTags)
            {
                rank = -1;
                section = new SectionSpec("category:" + list.GetTagDisplayName(i), title, 1000 + i);
            }
            else
            {
                Piece.UsageTagFlags flag = list.GetTagById(id);
                rank = IndexOr(Priority, flag, Priority.Length);
                section = new SectionSpec(flag.ToString(), title, IndexOr(DisplayOrder, flag, 500 + i));
                byFlag[flag] = section;
            }

            sections.Add(section);
            TagPieces.Clear();
            list.GetAvailablePiecesWithTag(id, table, TagPieces);
            foreach (Piece piece in TagPieces)
            {
                if (piece == null || piece.m_repairPiece || piece.m_removePiece)
                {
                    continue;
                }

                if (!placed.TryGetValue(piece, out (int Rank, SectionSpec Section) current) || rank < current.Rank)
                {
                    placed[piece] = (rank, section);
                }
            }
        }

        TagPieces.Clear();
        SectionSpec other = null;
        foreach (Piece piece in pieces)
        {
            if (Overrides.TryGetValue(piece.gameObject.name, out Piece.UsageTagFlags moved)
                && byFlag.TryGetValue(moved, out SectionSpec target) && placed.TryGetValue(piece, out (int Rank, SectionSpec Section) current)
                && current.Rank >= 0)
            {
                // Pieces a mod put in its own category stay there.
                placed[piece] = (current.Rank, target);
            }

            if (placed.TryGetValue(piece, out (int Rank, SectionSpec Section) entry))
            {
                entry.Section.Pieces.Add(piece);
            }
            else
            {
                other ??= new SectionSpec(OtherKey, Localization.instance.Localize("$betterhammer_menu_other"), int.MaxValue);
                other.Pieces.Add(piece);
            }
        }

        if (other != null)
        {
            sections.Add(other);
        }

        _ = sections.RemoveAll(section => section.Pieces.Count == 0);
        sections.Sort((a, b) => a.Order.CompareTo(b.Order));
        return sections;
    }

    private static int IndexOr(Piece.UsageTagFlags[] array, Piece.UsageTagFlags flag, int fallback)
    {
        int index = Array.IndexOf(array, flag);
        return index >= 0 ? index : fallback;
    }
}

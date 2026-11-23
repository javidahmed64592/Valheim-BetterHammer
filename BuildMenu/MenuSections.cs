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
/// Splits the pieces a build menu tab shows into sections.
/// <list type="bullet">
/// <item>Categories → Show All, and Building: one section per category.</item>
/// <item>Any other category: its sections from <see cref="SectionRules"/>, such as Beams and Poles.</item>
/// <item>Modded: one section per mod.</item>
/// </list>
/// Wherever modded pieces show, they're also gathered in a Modded section at the bottom. The other tabs
/// are split too: Materials' Show All by biome and a material by category, and Recent by category.
/// </summary>
internal static class MenuSections
{
    private const int LastOrder = int.MaxValue - 1;

    /// <summary>
    /// The sections for a tab's current tag.
    /// </summary>
    /// <param name="list">The tab's piece list.</param>
    /// <param name="tagId">The selected tag, or -1 for Show All.</param>
    /// <param name="table">The build tool's piece table.</param>
    /// <param name="pieces">The pieces the game shows, in its order. Every one ends up in a section.</param>
    /// <param name="categories">The Categories tab's list, for sorting other tabs' pieces into categories.</param>
    internal static List<SectionSpec> Build(IPieceList list, int tagId, PieceTable table, IReadOnlyList<Piece> pieces, ByUsagePieceList categories)
    {
        string tab = TabKey(list);
        List<SectionSpec> sections = Group(list, tagId, table, pieces, MenuState.GroupOf(tab), categories);
        MenuView.Sort(sections, MenuState.SortOf(tab));
        return sections;
    }

    /// <summary>
    /// Names a tab for remembering how it's grouped and sorted.
    /// </summary>
    internal static string TabKey(IPieceList list)
    {
        return list.GetType().Name;
    }

    private static List<SectionSpec> Group(IPieceList list, int tagId, PieceTable table, IReadOnlyList<Piece> pieces, GroupMode mode, ByUsagePieceList categories)
    {
        string view = ViewKey(list, tagId, table);
        if (mode is GroupMode.Material or GroupMode.Station)
        {
            return MenuView.GroupBy(pieces, mode, view);
        }

        if (mode == GroupMode.None)
        {
            return [Single(pieces)];
        }

        // Materials: Show All by the biome of each piece's main material, and a material by category.
        if (list is ByMaterialPieceList)
        {
            return tagId == -1 || categories == null || table == null
                ? MenuView.GroupByBiome(pieces, view)
                : ByCategory(categories, table, pieces, view);
        }

        // Recent pieces by category, most recent first in each.
        if (list is RecentPieceList && categories != null && table != null)
        {
            return ByCategory(categories, table, pieces, "recent");
        }

        if (list is ByUsagePieceList usage && list.ShowTags)
        {
            if (tagId == PieceCategories.ModdedTagId)
            {
                return ByMod(pieces);
            }

            CategoryInfo category = tagId == -1 ? null : PieceCategories.Find(usage, table, tagId);
            if (tagId == -1 || category?.Flag == Piece.UsageTagFlags.Building)
            {
                return WithModded(ByCategory(usage, table, pieces, category?.Key ?? "all"), pieces, category?.Key ?? "all");
            }

            if (category != null && category.Flag != 0)
            {
                return WithModded(ByType(category, pieces), pieces, category.Key);
            }
        }

        return [Single(pieces)];
    }

    private static SectionSpec Single(IReadOnlyList<Piece> pieces)
    {
        SectionSpec single = new("all", "", 0);
        single.Pieces.AddRange(pieces);
        return single;
    }

    /// <summary>
    /// Names what the tab shows (Show All, a category), so collapsed sections are remembered per view.
    /// </summary>
    private static string ViewKey(IPieceList list, int tagId, PieceTable table)
    {
        return tagId switch
        {
            -1 => "all",
            _ when list is ByUsagePieceList usage && PieceCategories.Find(usage, table, tagId) is CategoryInfo category => category.Key,
            _ => tagId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static List<SectionSpec> ByCategory(ByUsagePieceList list, PieceTable table, IReadOnlyList<Piece> pieces, string view)
    {
        Dictionary<CategoryInfo, SectionSpec> byCategory = [];
        SectionSpec other = null;
        foreach (Piece piece in pieces)
        {
            CategoryInfo category = PieceCategories.Of(list, table, piece);
            SectionSpec section;
            if (category == null)
            {
                section = other ??= Other(view);
            }
            else if (!byCategory.TryGetValue(category, out section))
            {
                section = new SectionSpec(view + "/" + category.Key, category.Title, category.Order);
                byCategory[category] = section;
            }

            section.Pieces.Add(piece);
        }

        List<SectionSpec> sections = [.. byCategory.Values];
        if (other != null)
        {
            sections.Add(other);
        }

        sections.Sort((a, b) => a.Order.CompareTo(b.Order));
        return sections;
    }

    private static List<SectionSpec> ByType(CategoryInfo category, IReadOnlyList<Piece> pieces)
    {
        Dictionary<SectionRules.Section, SectionSpec> byRule = [];
        SectionSpec unsorted = null;
        foreach (Piece piece in pieces)
        {
            SectionRules.Section rule = SectionRules.SectionOf(category.Flag, piece.gameObject.name);
            SectionSpec section;
            if (rule == null)
            {
                // Without any sections, the category is one section under its own name.
                unsorted ??= !SectionRules.HasSections(category.Flag)
                    ? new SectionSpec(category.Key + "/" + category.Key, category.Title, 0)
                    : Other(category.Key);
                section = unsorted;
            }
            else if (!byRule.TryGetValue(rule, out section))
            {
                section = new SectionSpec(category.Key + "/" + rule.Name, Localization.instance.Localize(rule.Name), rule.Position);
                byRule[rule] = section;
            }

            section.Pieces.Add(piece);
        }

        List<SectionSpec> sections = [.. byRule.Values];
        if (unsorted != null)
        {
            sections.Add(unsorted);
        }

        sections.Sort((a, b) => a.Order.CompareTo(b.Order));
        return sections;
    }

    private static List<SectionSpec> ByMod(IReadOnlyList<Piece> pieces)
    {
        Dictionary<string, SectionSpec> byMod = [];
        foreach (Piece piece in pieces)
        {
            string mod = ModdedPieces.SourceOf(piece) ?? "?";
            if (!byMod.TryGetValue(mod, out SectionSpec section))
            {
                section = new SectionSpec("modded/" + mod, mod, 0);
                byMod[mod] = section;
            }

            section.Pieces.Add(piece);
        }

        List<SectionSpec> sections = [.. byMod.Values];
        sections.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        return sections;
    }

    /// <summary>
    /// Adds a Modded section at the bottom with every modded piece shown above, again.
    /// </summary>
    private static List<SectionSpec> WithModded(List<SectionSpec> sections, IReadOnlyList<Piece> pieces, string view)
    {
        SectionSpec modded = null;
        foreach (Piece piece in pieces)
        {
            if (ModdedPieces.IsModded(piece))
            {
                modded ??= new SectionSpec(view + "/modded", PieceCategories.Modded.Title, int.MaxValue);
                modded.Pieces.Add(piece);
            }
        }

        if (modded != null)
        {
            sections.Add(modded);
        }

        return sections;
    }

    private static SectionSpec Other(string view)
    {
        return new SectionSpec(view + "/other", Localization.instance.Localize("$betterhammer_menu_other"), LastOrder);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterHammer.BuildMenu;

/// <summary>
/// How a build menu tab splits its pieces into sections.
/// </summary>
internal enum GroupMode
{
    /// <summary>
    /// The tab's own sections: categories in Show All, the rules' sections in a category, mods in Modded.
    /// </summary>
    Type = 0,
    Material = 1,
    Station = 2,
    None = 3,
}

/// <summary>
/// How pieces are ordered inside each section.
/// </summary>
internal enum SortMode
{
    /// <summary>
    /// The game's order: the build tool's order, or most recent first in the Recent tab.
    /// </summary>
    Default = 0,
    Name = 1,
    Material = 2,
    Station = 3,
}

/// <summary>
/// Grouping and sorting pieces by name, main material, crafting station and cost.
/// </summary>
internal static class MenuView
{
    internal static readonly GroupMode[] GroupModes = [GroupMode.Type, GroupMode.Material, GroupMode.Station, GroupMode.None];

    internal static readonly SortMode[] SortModes = [SortMode.Default, SortMode.Name, SortMode.Material, SortMode.Station];

    /// <summary>
    /// Sorting by what the pieces are already grouped by would do nothing, so it isn't offered.
    /// </summary>
    internal static bool Allowed(GroupMode group, SortMode sort)
    {
        return !(group == GroupMode.Material && sort == SortMode.Material)
            && !(group == GroupMode.Station && sort == SortMode.Station);
    }

    /// <summary>
    /// The sort options that make sense with the grouping.
    /// </summary>
    internal static SortMode[] SortOptions(GroupMode group)
    {
        return [.. SortModes.Where(sort => Allowed(group, sort))];
    }

    internal static string Label(GroupMode mode)
    {
        return mode switch
        {
            GroupMode.Material => "$betterhammer_view_material",
            GroupMode.Station => "$betterhammer_view_station",
            GroupMode.None => "$betterhammer_view_none",
            _ => "$betterhammer_view_type",
        };
    }

    internal static string Label(SortMode mode)
    {
        return mode switch
        {
            SortMode.Name => "$betterhammer_view_name",
            SortMode.Material => "$betterhammer_view_material",
            SortMode.Station => "$betterhammer_view_station",
            _ => "$betterhammer_view_default",
        };
    }

    /// <summary>
    /// Splits the pieces by their main material or crafting station. Materials follow the biomes in
    /// <see cref="SectionRules"/>; stations follow the order each first appears in, so the workbench
    /// comes first.
    /// </summary>
    internal static List<SectionSpec> GroupBy(IReadOnlyList<Piece> pieces, GroupMode mode, string view)
    {
        Dictionary<string, SectionSpec> sections = [];
        List<SectionSpec> ordered = [];
        foreach (Piece piece in pieces)
        {
            (string key, string title) = mode == GroupMode.Material ? MainMaterial(piece) : Station(piece);
            if (!sections.TryGetValue(key, out SectionSpec section))
            {
                int order = mode == GroupMode.Material ? MaterialOrder(key) : ordered.Count;
                section = new SectionSpec(view + "/" + mode.ToString().ToLowerInvariant() + "/" + key, title, order);
                sections[key] = section;
                ordered.Add(section);
            }

            section.Pieces.Add(piece);
        }

        // Stable, so materials in the same place keep the order they first appeared in.
        return [.. ordered.OrderBy(section => section.Order)];
    }

    /// <summary>
    /// Splits the pieces by the biome their main material comes from, in biome order.
    /// </summary>
    internal static List<SectionSpec> GroupByBiome(IReadOnlyList<Piece> pieces, string view)
    {
        Dictionary<int, SectionSpec> sections = [];
        foreach (Piece piece in pieces)
        {
            SectionRules.Biome biome = SectionRules.BiomeOf(MainMaterial(piece).Key);
            if (!sections.TryGetValue(biome.Index, out SectionSpec section))
            {
                section = new SectionSpec(view + "/biome/" + biome.Name, Localization.instance.Localize(biome.Name), biome.Index);
                sections[biome.Index] = section;
            }

            section.Pieces.Add(piece);
        }

        return [.. sections.Values.OrderBy(section => section.Order)];
    }

    /// <summary>
    /// A material's place in biome order, for sorting: its biome, then its place in the biome's list.
    /// </summary>
    private static int MaterialOrder(string itemPrefabName)
    {
        SectionRules.Biome biome = SectionRules.BiomeOf(itemPrefabName);
        return (biome.Index * 1000) + biome.ItemIndex;
    }

    /// <summary>
    /// Orders each section's pieces. Ties keep the game's order.
    /// </summary>
    internal static void Sort(List<SectionSpec> sections, SortMode mode)
    {
        if (mode == SortMode.Default)
        {
            return;
        }

        foreach (SectionSpec section in sections)
        {
            List<Piece> sorted = mode switch
            {
                SortMode.Material => [.. section.Pieces.OrderBy(p => MaterialOrder(MainMaterial(p).Key)).ThenBy(p => MainMaterial(p).Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(Name, StringComparer.CurrentCultureIgnoreCase)],
                SortMode.Station => [.. section.Pieces.OrderBy(p => Station(p).Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(Name, StringComparer.CurrentCultureIgnoreCase)],
                _ => [.. section.Pieces.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase)],
            };
            section.Pieces.Clear();
            section.Pieces.AddRange(sorted);
        }
    }

    private static string Name(Piece piece)
    {
        return Localization.instance.Localize(piece.m_name);
    }

    /// <summary>
    /// The material the piece needs most of (the first listed on a tie), by prefab name and shown name.
    /// </summary>
    private static (string Key, string Title) MainMaterial(Piece piece)
    {
        Piece.Requirement main = null;
        foreach (Piece.Requirement requirement in piece.m_resources ?? [])
        {
            if (requirement?.m_resItem != null && (main == null || requirement.m_amount > main.m_amount))
            {
                main = requirement;
            }
        }

        return main == null
            ? ("none", Localization.instance.Localize("$betterhammer_view_no_material"))
            : (main.m_resItem.gameObject.name, Localization.instance.Localize(main.m_resItem.m_itemData.m_shared.m_name));
    }

    private static (string Key, string Title) Station(Piece piece)
    {
        CraftingStation station = piece.m_craftingStation;
        return station == null
            ? ("none", Localization.instance.Localize("$betterhammer_view_no_station"))
            : (station.m_name, Localization.instance.Localize(station.m_name));
    }
}

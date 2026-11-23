using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterHammer.BuildMenu;

/// <summary>
/// The Materials tab's list on the left, in biome order: the game lists its main materials, a divider,
/// then the rest by name. Here each biome's materials follow each other, in the order the rules list
/// them, under a header (see <see cref="TagListHeaders"/>), and materials from other mods come last.
/// </summary>
internal static class MaterialTags
{
    internal static void Sort(ByMaterialPieceList list)
    {
        List<ByMaterialPieceList.RequirementTagData> sorted = [.. list.m_requirementTags
            .OrderBy(tag => SectionRules.BiomeOf(tag.m_netObjectName).Index)
            .ThenBy(tag => SectionRules.BiomeOf(tag.m_netObjectName).ItemIndex)
            .ThenBy(tag => tag.m_displayNameLocalized, StringComparer.CurrentCultureIgnoreCase)];
        list.m_requirementTags.Clear();
        list.m_requirementTags.AddRange(sorted);

        // No divider between the main materials and the rest: the biome headers replace it.
        list.m_availableMajorMaterials = -1;
        list.UpdateLookups();
    }

    /// <summary>
    /// The biome of the material a tag of the Materials tab stands for.
    /// </summary>
    internal static SectionRules.Biome BiomeOfTag(ByMaterialPieceList list, int tagId)
    {
        return SectionRules.BiomeOf(list.GetTagById(tagId)?.m_resItem?.gameObject.name);
    }
}

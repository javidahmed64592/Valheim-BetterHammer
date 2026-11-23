using System.Collections.Generic;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Group headers in the Categories tab's list on the left: Construction, Utility, Comfort, Food and Mods,
/// each above its categories. They're plain labels, so keyboard and gamepad navigation skip them.
/// </summary>
internal sealed class TagListHeaders
{
    private const float Height = 24f;
    private const float FontScale = 0.8f;

    private readonly BuildUi _ui;
    private readonly RectTransform _parking;
    private readonly TextMeshProUGUI _fontSource;
    private readonly List<RectTransform> _headers = [];

    internal TagListHeaders(BuildUi ui, TextMeshProUGUI fontSource)
    {
        _ui = ui;
        _fontSource = fontSource;

        // Unused headers wait here, outside the list, so they don't shift the game's sibling indices.
        GameObject parking = new("BetterHammer_TagHeaders", typeof(RectTransform));
        parking.SetActive(false);
        _parking = (RectTransform)parking.transform;
        _parking.SetParent(ui.transform, false);
    }

    /// <summary>
    /// Takes every header out of the list, before the game rebuilds it.
    /// </summary>
    internal void Park()
    {
        foreach (RectTransform header in _headers)
        {
            header.SetParent(_parking, false);
        }
    }

    /// <summary>
    /// Puts a header above the first category of each group, after the game rebuilt the list.
    /// </summary>
    internal void Place()
    {
        Park();
        if (_ui.m_pieceLists[_ui.m_currentPieceList] is not ByUsagePieceList list
            || !_ui.m_tagListContainer.gameObject.activeSelf
            || _ui.m_currentBuildTool == null)
        {
            return;
        }

        int used = 0;
        CategoryGroup? last = null;
        foreach (BuildUiTagButton button in _ui.m_tagButtons)
        {
            CategoryGroup group = PieceCategories.Find(list, _ui.m_currentBuildTool, button.m_tagId)?.Group ?? CategoryGroup.Mods;
            if (group == last)
            {
                continue;
            }

            last = group;
            RectTransform header = Take(used++);
            header.GetComponent<TextMeshProUGUI>().text = Localization.instance.Localize(Token(group));
            header.SetParent(_ui.m_tagListScrollContent, false);
            header.SetSiblingIndex(button.transform.GetSiblingIndex());
        }
    }

    private static string Token(CategoryGroup group)
    {
        return group switch
        {
            CategoryGroup.Construction => "$betterhammer_group_construction",
            CategoryGroup.Utility => "$betterhammer_group_utility",
            CategoryGroup.Comfort => "$betterhammer_group_comfort",
            CategoryGroup.Food => "$betterhammer_group_food",
            _ => "$betterhammer_group_mods",
        };
    }

    private RectTransform Take(int index)
    {
        while (_headers.Count <= index)
        {
            _headers.Add(Create());
        }

        return _headers[index];
    }

    private RectTransform Create()
    {
        GameObject header = new("GroupHeader", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        RectTransform rect = (RectTransform)header.transform;
        rect.SetParent(_parking, false);
        float width = ((RectTransform)_ui.m_showAllTagsButton.transform).rect.width;
        rect.sizeDelta = new Vector2(width, Height);

        LayoutElement element = header.GetComponent<LayoutElement>();
        element.minHeight = Height;
        element.preferredHeight = Height;

        TextMeshProUGUI text = header.GetComponent<TextMeshProUGUI>();
        if (_fontSource != null)
        {
            text.font = _fontSource.font;
            text.fontSharedMaterial = _fontSource.fontSharedMaterial;
            text.fontSize = _fontSource.fontSize * FontScale;
        }

        text.color = SectionedGrid.TitleColor;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.margin = new Vector4(4f, 0f, 0f, 2f);
        text.raycastTarget = false;
        return rect;
    }
}

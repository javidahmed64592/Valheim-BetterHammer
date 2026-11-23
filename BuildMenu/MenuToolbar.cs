using System;

using TMPro;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterHammer.BuildMenu;

/// <summary>
/// A label that cycles through options: left-click for the next, right-click for the previous.
/// </summary>
internal sealed class CycleLabel : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private TextMeshProUGUI _text;
    private string _caption;
    private string _value;
    private bool _hovered;

    internal event Action<int> Cycled;

    internal void Init(TextMeshProUGUI text, string caption)
    {
        _text = text;
        _caption = caption;
    }

    internal void Show(string value)
    {
        _value = value;
        Redraw();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            Cycled?.Invoke(1);
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            Cycled?.Invoke(-1);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        Redraw();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        Redraw();
    }

    private void Redraw()
    {
        if (_text != null)
        {
            _text.text = Localization.instance.Localize(_caption) + "  <color=" + (_hovered ? "#FFE08A" : "#FFFFFF") + ">" + _value + "</color>";
        }
    }
}

/// <summary>
/// A bar above the piece grid with "Group" and "Sort" labels for the current tab. The piece grid is made
/// shorter to make room, and the bar follows its left edge, which moves when the tab has no list of
/// categories.
/// </summary>
internal sealed class MenuToolbar
{
    private const float Height = 26f;
    private const float Gap = 4f;
    private const float Spacing = 28f;
    private const float FontScale = 0.85f;

    private readonly BuildUi _ui;
    private readonly CycleLabel _group;
    private readonly CycleLabel _sort;
    private readonly Action _changed;

    internal MenuToolbar(BuildUi ui, TextMeshProUGUI fontSource, Action changed)
    {
        _ui = ui;
        _changed = changed;

        RectTransform pieceView = ui.m_pieceView;
        pieceView.offsetMax = new Vector2(pieceView.offsetMax.x, pieceView.offsetMax.y - Height - Gap);

        GameObject bar = new("BetterHammer_Toolbar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        RectTransform rect = (RectTransform)bar.transform;
        rect.SetParent(pieceView, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, Gap);
        rect.sizeDelta = new Vector2(0f, Height);

        HorizontalLayoutGroup layout = bar.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = Spacing;
        layout.padding = new RectOffset(6, 6, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        _group = CreateLabel(rect, fontSource, "$betterhammer_view_group");
        _sort = CreateLabel(rect, fontSource, "$betterhammer_view_sort");
        _group.Cycled += step => Change(sort: false, step);
        _sort.Cycled += step => Change(sort: true, step);
    }

    /// <summary>
    /// Shows the current tab's choices.
    /// </summary>
    internal void Refresh()
    {
        string tab = CurrentTab();
        _group.Show(Localization.instance.Localize(MenuView.Label(MenuState.GroupOf(tab))));
        _sort.Show(Localization.instance.Localize(MenuView.Label(MenuState.SortOf(tab))));
    }

    private string CurrentTab()
    {
        return MenuSections.TabKey(_ui.m_pieceLists[_ui.m_currentPieceList]);
    }

    /// <summary>
    /// Steps the grouping or the sorting by one option either way.
    /// </summary>
    private void Change(bool sort, int step)
    {
        string tab = CurrentTab();
        GroupMode groupMode = MenuState.GroupOf(tab);
        SortMode sortMode = MenuState.SortOf(tab);
        if (sort)
        {
            sortMode = Step(MenuView.SortOptions(groupMode), sortMode, step);
        }
        else
        {
            groupMode = Step(MenuView.GroupModes, groupMode, step);
            if (!MenuView.Allowed(groupMode, sortMode))
            {
                sortMode = SortMode.Default;
            }
        }

        MenuState.SetView(tab, groupMode, sortMode);
        Player.m_localPlayer?.PlayButtonSound();
        Refresh();
        _changed();
    }

    private static T Step<T>(T[] options, T current, int step)
    {
        int index = Array.IndexOf(options, current);
        int next = ((index < 0 ? 0 : index) + step) % options.Length;
        return options[next < 0 ? next + options.Length : next];
    }

    private static CycleLabel CreateLabel(RectTransform parent, TextMeshProUGUI fontSource, string caption)
    {
        GameObject label = new("Cycle", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
        label.transform.SetParent(parent, false);
        label.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
        if (fontSource != null)
        {
            text.font = fontSource.font;
            text.fontSharedMaterial = fontSource.fontSharedMaterial;
            text.fontSize = fontSource.fontSize * FontScale;
        }

        text.color = SectionedGrid.TitleColor;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.richText = true;
        text.raycastTarget = true;

        CycleLabel cycle = label.AddComponent<CycleLabel>();
        cycle.Init(text, caption);
        return cycle;
    }
}

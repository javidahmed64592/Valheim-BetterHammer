using System.Collections.Generic;

using TMPro;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Replaces the build menu's single piece grid with sections, each a header above its own grid. The game
/// still creates, pools and sets up the piece buttons; after every update they're moved into the section
/// grids. A piece shown in more than one section gets an extra button for each further section.
/// </summary>
internal sealed class SectionedGrid
{
    private const float HeaderHeight = 30f;
    private const float SectionSpacing = 6f;
    private const float ArrowSize = 12f;

    /// <summary>
    /// The orange of the build menu's active tab.
    /// </summary>
    internal static readonly Color TitleColor = new(1f, 0.682f, 0f);

    private sealed class Section
    {
        internal GameObject Root { get; set; }
        internal GameObject Header { get; set; }
        internal RectTransform Grid { get; set; }
        internal GridLayoutGroup Layout { get; set; }
        internal TextMeshProUGUI Title { get; set; }
        internal RectTransform Arrow { get; set; }
        internal string Key { get; set; }
        internal string TitleText { get; set; }
        internal List<BuildUiPieceButton> Buttons { get; } = [];
    }

    private readonly BuildUi _ui;
    private readonly RectTransform _root;
    private readonly RectTransform _pool;
    private readonly GridLayoutGroup _template;
    private readonly TextMeshProUGUI _fontSource;
    private readonly Image _dividerSource;

    /// <summary>
    /// The menu's search box (the game's own input field type, which derives from this one).
    /// </summary>
    private readonly TMP_InputField _search;
    private readonly MenuToolbar _toolbar;
    private readonly List<Section> _sections = [];
    private readonly List<BuildUiPieceButton> _extras = [];
    private readonly Stack<BuildUiPieceButton> _extraPool = new();
    private readonly List<Piece> _pieces = [];
    private readonly List<List<Selectable>> _rows = [];
    private int _used;
    private int _updateDepth;

    private SectionedGrid(BuildUi ui)
    {
        _ui = ui;
        _pool = ui.m_pieceButtonsContainer;
        _template = _pool.GetComponent<GridLayoutGroup>();
        _fontSource = FindLabel(ui.m_showAllTagsButton);
        TagHeaders = new TagListHeaders(ui, _fontSource);
        _dividerSource = ui.m_materialSeparator != null ? ui.m_materialSeparator.GetComponent<Image>() : null;
        _search = ui.m_tagListContainer != null ? ui.m_tagListContainer.GetComponentInChildren<TMP_InputField>(true) : null;
        _toolbar = new MenuToolbar(ui, _fontSource, _search, Rebuild);
        MenuLayout.Apply(ui);

        GameObject root = new("BetterHammer_Sections", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        _root = (RectTransform)root.transform;
        _root.SetParent(_pool.parent, false);
        _root.anchorMin = new Vector2(0f, 1f);
        _root.anchorMax = Vector2.one;
        _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = Vector2.zero;
        _root.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
        RectOffset padding = _template.padding;
        layout.padding = new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
        layout.spacing = SectionSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The game's own container stays as the place unused buttons wait, out of the scroll view.
        ui.m_pieceScrollRect.content = _root;
    }

    internal static SectionedGrid Current { get; private set; }

    /// <summary>
    /// The group headers in the list of categories on the left.
    /// </summary>
    internal TagListHeaders TagHeaders { get; }

    /// <summary>
    /// The sectioned grid of this build menu, or null when sections are off.
    /// </summary>
    internal static SectionedGrid For(BuildUi ui)
    {
        return Current != null && Current._ui == ui ? Current : null;
    }

    internal static void Attach(BuildUi ui)
    {
        if (BuildMenuConfig.Sections && ui.m_pieceButtonsContainer != null && ui.m_pieceScrollRect != null
            && ui.m_pieceButtonsContainer.GetComponent<GridLayoutGroup>() != null)
        {
            Current = new SectionedGrid(ui);
        }
    }

    /// <summary>
    /// True while the game is filling the grid, when the sections are out of date.
    /// </summary>
    internal bool Updating => _updateDepth > 0;

    internal void BeginUpdate()
    {
        _updateDepth++;
    }

    /// <summary>
    /// Called after the game has filled its button list. Updates can nest (a search can restart one), so
    /// only the outermost rebuilds the sections, from the game's final list.
    /// </summary>
    internal void EndUpdate()
    {
        if (--_updateDepth > 0)
        {
            return;
        }

        _updateDepth = 0;
        Rebuild();
    }

    private void Rebuild()
    {
        _toolbar.Refresh();
        foreach (BuildUiPieceButton extra in _extras)
        {
            extra.gameObject.SetActive(false);
            extra.transform.SetParent(_pool, false);
            _extraPool.Push(extra);
        }

        _extras.Clear();

        _pieces.Clear();
        Dictionary<Piece, BuildUiPieceButton> unplaced = [];
        foreach (BuildUiPieceButton button in _ui.m_pieceButtons)
        {
            _pieces.Add(button.Piece);
            unplaced[button.Piece] = button;
        }

        IPieceList list = _ui.m_pieceLists[_ui.m_currentPieceList];
        List<SectionSpec> specs = MenuSections.Build(list, _ui.m_currentTagId, _ui.m_currentBuildTool, _pieces, CategoriesList());
        bool headers = specs.Count > 1;
        string scope = list.GetType().Name + "/";

        _used = 0;
        Section section = null;
        foreach (SectionSpec spec in specs)
        {
            section = Take();
            section.Key = scope + spec.Key;
            section.TitleText = spec.Title;
            section.Header.SetActive(headers);
            foreach (Piece piece in spec.Pieces)
            {
                if (unplaced.TryGetValue(piece, out BuildUiPieceButton button))
                {
                    _ = unplaced.Remove(piece);
                }
                else
                {
                    button = Extra(piece);
                }

                Place(section, button);
            }
        }

        // Anything the sections missed still has to be reachable.
        foreach (BuildUiPieceButton button in unplaced.Values)
        {
            section ??= Take();
            Place(section, button);
        }

        for (int i = _used; i < _sections.Count; i++)
        {
            _sections[i].Root.SetActive(false);
            _sections[i].Buttons.Clear();
        }

        foreach (BuildUiPieceButton pooled in _ui.m_pieceButtonPool)
        {
            pooled.transform.SetParent(_pool, false);
        }

        Refresh();
    }

    private ByUsagePieceList CategoriesList()
    {
        foreach (IPieceList list in _ui.m_pieceLists)
        {
            if (list is ByUsagePieceList categories)
            {
                return categories;
            }
        }

        return null;
    }

    private static void Place(Section section, BuildUiPieceButton button)
    {
        button.transform.SetParent(section.Grid, false);
        button.transform.SetAsLastSibling();
        section.Buttons.Add(button);
    }

    private BuildUiPieceButton Extra(Piece piece)
    {
        BuildUiPieceButton button = _extraPool.Count > 0
            ? _extraPool.Pop()
            : Object.Instantiate(_ui.m_pieceButtonPrefab, _pool).GetComponent<BuildUiPieceButton>();

        button.gameObject.SetActive(true);
        button.Setup(piece, _ui);
        _extras.Add(button);
        return button;
    }

    /// <summary>
    /// Applies the search to the extra buttons (the game already did its own), hides empty sections,
    /// shows or hides collapsed grids, and redoes the gamepad navigation.
    /// </summary>
    internal void Refresh()
    {
        if (Updating)
        {
            return;
        }

        string term = SearchTerm();
        foreach (BuildUiPieceButton extra in _extras)
        {
            extra.gameObject.SetActive(Matches(extra, term));
        }

        for (int i = 0; i < _used; i++)
        {
            Section section = _sections[i];
            int shown = 0;
            foreach (BuildUiPieceButton button in section.Buttons)
            {
                if (button.gameObject.activeSelf)
                {
                    shown++;
                }
            }

            // While searching every section is open, so no match is hidden.
            bool collapsed = term.Length == 0 && section.Header.activeSelf && MenuState.IsCollapsed(section.Key);
            section.Root.SetActive(shown > 0);
            section.Grid.gameObject.SetActive(!collapsed);
            section.Title.text = section.TitleText + "  <alpha=#99>" + shown;
            section.Arrow.localEulerAngles = new Vector3(0f, 0f, collapsed ? 90f : 0f);
        }

        _ui.ConfigureButtonNavigation();
    }

    private string SearchTerm()
    {
        return _search != null && _search.isActiveAndEnabled
            ? _search.text.Replace(" ", "").ToLowerInvariant()
            : "";
    }

    /// <summary>
    /// The game's search: the term appears in the piece's name or one of its materials.
    /// </summary>
    private static bool Matches(BuildUiPieceButton button, string term)
    {
        if (term.Length == 0)
        {
            return true;
        }

        foreach (string searchTerm in button.LocalizedSearchTerms)
        {
            if (searchTerm.Contains(term))
            {
                return true;
            }
        }

        return false;
    }

    private void Toggle(Section section)
    {
        if (SearchTerm().Length > 0)
        {
            return;
        }

        MenuState.SetCollapsed(section.Key, !MenuState.IsCollapsed(section.Key));
        Refresh();
    }

    /// <summary>
    /// Every shown piece button, top to bottom.
    /// </summary>
    internal IEnumerable<BuildUiPieceButton> ShownButtons()
    {
        for (int i = 0; i < _used; i++)
        {
            Section section = _sections[i];
            if (!section.Root.activeSelf || !section.Grid.gameObject.activeSelf)
            {
                continue;
            }

            foreach (BuildUiPieceButton button in section.Buttons)
            {
                if (button.gameObject.activeSelf)
                {
                    yield return button;
                }
            }
        }
    }

    internal GameObject FirstButton()
    {
        foreach (BuildUiPieceButton button in ShownButtons())
        {
            return button.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Refreshes the favourite star on extra buttons, which the game doesn't know about.
    /// </summary>
    internal void RefreshFavorite(string pieceName)
    {
        foreach (BuildUiPieceButton extra in _extras)
        {
            if (extra.Piece != null && extra.Piece.gameObject.name == pieceName)
            {
                extra.RefreshFavorite();
            }
        }
    }

    /// <summary>
    /// Gamepad and arrow-key navigation through the sections as if they were one grid: left and right
    /// along a row, up and down to the same column of the row above or below, across headers. Left from
    /// the first column goes to the selected category, as in the game.
    /// </summary>
    internal void ConfigureNavigation(Selectable left)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_root);

        _rows.Clear();
        for (int i = 0; i < _used; i++)
        {
            Section section = _sections[i];
            if (!section.Root.activeSelf || !section.Grid.gameObject.activeSelf)
            {
                continue;
            }

            int columns = Columns(section);
            List<Selectable> row = null;
            foreach (BuildUiPieceButton button in section.Buttons)
            {
                Selectable selectable = button.gameObject.activeSelf ? button.GetComponent<Selectable>() : null;
                if (selectable == null || !selectable.IsInteractable())
                {
                    continue;
                }

                if (row == null || row.Count == columns)
                {
                    row = [];
                    _rows.Add(row);
                }

                row.Add(selectable);
            }
        }

        for (int r = 0; r < _rows.Count; r++)
        {
            List<Selectable> row = _rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                Navigation navigation = row[c].navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnLeft = c > 0 ? row[c - 1] : left;
                navigation.selectOnRight = c + 1 < row.Count ? row[c + 1] : null;
                navigation.selectOnUp = r > 0 ? At(_rows[r - 1], c) : null;
                navigation.selectOnDown = r + 1 < _rows.Count ? At(_rows[r + 1], c) : null;
                row[c].navigation = navigation;
            }
        }
    }

    private static Selectable At(List<Selectable> row, int column)
    {
        return row[Mathf.Min(column, row.Count - 1)];
    }

    private static int Columns(Section section)
    {
        GridLayoutGroup layout = section.Layout;
        if (layout.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
        {
            return Mathf.Max(1, layout.constraintCount);
        }

        float width = section.Grid.rect.width - layout.padding.horizontal;
        return Mathf.Max(1, Mathf.FloorToInt((width + layout.spacing.x + 0.01f) / (layout.cellSize.x + layout.spacing.x)));
    }

    private Section Take()
    {
        if (_used == _sections.Count)
        {
            _sections.Add(CreateSection());
        }

        Section section = _sections[_used++];
        section.Root.SetActive(true);
        section.Root.transform.SetSiblingIndex(_used - 1);
        section.Buttons.Clear();
        return section;
    }

    private Section CreateSection()
    {
        Section section = new()
        {
            Root = new GameObject("Section", typeof(RectTransform), typeof(VerticalLayoutGroup))
        };
        section.Root.transform.SetParent(_root, false);
        VerticalLayoutGroup layout = section.Root.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateHeader(section);

        GameObject grid = new("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        grid.transform.SetParent(section.Root.transform, false);
        section.Grid = (RectTransform)grid.transform;
        section.Layout = grid.GetComponent<GridLayoutGroup>();
        section.Layout.cellSize = _template.cellSize;
        section.Layout.spacing = _template.spacing;
        section.Layout.startCorner = _template.startCorner;
        section.Layout.startAxis = _template.startAxis;
        section.Layout.childAlignment = _template.childAlignment;
        section.Layout.constraint = _template.constraint;
        section.Layout.constraintCount = _template.constraintCount;
        section.Layout.padding = new RectOffset(0, 0, 0, 0);
        return section;
    }

    private void CreateHeader(Section section)
    {
        GameObject header = new("Header", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        header.transform.SetParent(section.Root.transform, false);
        section.Header = header;

        LayoutElement element = header.GetComponent<LayoutElement>();
        element.minHeight = HeaderHeight;
        element.preferredHeight = HeaderHeight;

        // Clear, but still catches clicks.
        header.GetComponent<Image>().color = Color.clear;
        Button button = header.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => Toggle(section));

        GameObject arrow = new("Arrow", typeof(RectTransform), typeof(Image));
        section.Arrow = (RectTransform)arrow.transform;
        section.Arrow.SetParent(header.transform, false);
        section.Arrow.anchorMin = new Vector2(0f, 0.5f);
        section.Arrow.anchorMax = new Vector2(0f, 0.5f);
        section.Arrow.pivot = new Vector2(0.5f, 0.5f);
        section.Arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);
        section.Arrow.anchoredPosition = new Vector2(4f + (ArrowSize / 2f), 1f);
        Image arrowImage = arrow.GetComponent<Image>();
        arrowImage.sprite = MenuSprites.DownArrow;
        arrowImage.color = TitleColor;
        arrowImage.raycastTarget = false;

        GameObject title = new("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform titleRect = (RectTransform)title.transform;
        titleRect.SetParent(header.transform, false);
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(ArrowSize + 12f, 2f);
        titleRect.offsetMax = Vector2.zero;
        section.Title = title.GetComponent<TextMeshProUGUI>();
        if (_fontSource != null)
        {
            section.Title.font = _fontSource.font;
            section.Title.fontSharedMaterial = _fontSource.fontSharedMaterial;
            section.Title.fontSize = _fontSource.fontSize;
        }

        section.Title.color = TitleColor;
        section.Title.alignment = TextAlignmentOptions.MidlineLeft;
        section.Title.textWrappingMode = TextWrappingModes.NoWrap;
        section.Title.richText = true;
        section.Title.raycastTarget = false;

        if (_dividerSource != null)
        {
            GameObject divider = new("Divider", typeof(RectTransform), typeof(Image));
            RectTransform dividerRect = (RectTransform)divider.transform;
            dividerRect.SetParent(header.transform, false);
            dividerRect.anchorMin = Vector2.zero;
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.sizeDelta = new Vector2(0f, ((RectTransform)_dividerSource.transform).sizeDelta.y);
            Image dividerImage = divider.GetComponent<Image>();
            dividerImage.sprite = _dividerSource.sprite;
            dividerImage.type = _dividerSource.type;
            dividerImage.color = _dividerSource.color;
            dividerImage.material = _dividerSource.material;
            dividerImage.raycastTarget = false;
        }
    }

    /// <summary>
    /// The tag button's normal label (not the one shown while it's selected), for its font.
    /// </summary>
    private static TextMeshProUGUI FindLabel(BuildUiTagButton button)
    {
        if (button == null)
        {
            return null;
        }

        TextMeshProUGUI fallback = null;
        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.transform.parent == button.transform)
            {
                return text;
            }

            fallback ??= text;
        }

        return fallback;
    }

    /// <summary>
    /// Selects the first shown piece, if any.
    /// </summary>
    internal bool SelectFirst()
    {
        GameObject first = FirstButton();
        if (first == null)
        {
            return false;
        }

        EventSystem.current.SetSelectedGameObject(first);
        return true;
    }
}

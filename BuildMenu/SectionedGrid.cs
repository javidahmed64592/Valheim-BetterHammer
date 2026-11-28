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
    /// <summary>
    /// The build menu's patches: sections, headers, grouping, favourites, hidden pieces and the Modded and Hidden
    /// categories. Without it, the build menu is vanilla's.
    /// </summary>
    internal const string Feature = "Sectioned build menu";

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
        internal int? FavoriteTag { get; set; }
        internal bool KeepEmpty { get; set; }
    }

    private readonly RectTransform _root;
    private readonly RectTransform _pool;
    private readonly GridLayoutGroup _template;
    private readonly TextMeshProUGUI _fontSource;
    private readonly Image _dividerSource;

    /// <summary>
    /// The menu's search box (the game's own input field type, which derives from this one).
    /// </summary>
    private readonly TMP_InputField _search;

    /// <summary>
    /// Inactive, so buttons of pieces that aren't shown (hidden, or not buildable with that filter on)
    /// stay invisible even when the game's search switches them on.
    /// </summary>
    private readonly RectTransform _setAside;
    private readonly MenuToolbar _toolbar;
    private readonly List<Section> _sections = [];
    private readonly List<BuildUiPieceButton> _extras = [];
    private readonly Stack<BuildUiPieceButton> _extraPool = new();
    private readonly List<Piece> _pieces = [];
    private readonly List<Piece> _counted = [];
    private readonly List<List<Selectable>> _rows = [];
    private int _used;
    private int _updateDepth;

    private SectionedGrid(BuildUi ui)
    {
        Ui = ui;
        _pool = ui.m_pieceButtonsContainer;
        _template = _pool.GetComponent<GridLayoutGroup>();
        _fontSource = FindLabel(ui.m_showAllTagsButton);
        TagHeaders = new TagListHeaders(ui, _fontSource);
        _dividerSource = ui.m_materialSeparator != null ? ui.m_materialSeparator.GetComponent<Image>() : null;
        // Read by name: its type is in an assembly the mod doesn't reference.
        _search = HarmonyLib.AccessTools.Field(typeof(BuildUi), "m_searchField")?.GetValue(ui) as TMP_InputField;
        _toolbar = new MenuToolbar(ui, _fontSource, _search, Rebuild);
        UI.ClearButton.Add(_search);

        // The game's own listener runs first and filters its buttons; then the sections follow the new search.
        _search?.onValueChanged.AddListener(_ => Refresh());
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

        GameObject setAside = new("BetterHammer_SetAside", typeof(RectTransform));
        setAside.SetActive(false);
        _setAside = (RectTransform)setAside.transform;
        _setAside.SetParent(_pool.parent, false);

        // The game's own container stays as the place unused buttons wait, out of the scroll view.
        ui.m_pieceScrollRect.content = _root;
    }

    internal static SectionedGrid Current { get; private set; }

    internal BuildUi Ui { get; }

    /// <summary>
    /// The group headers in the list of categories on the left.
    /// </summary>
    internal TagListHeaders TagHeaders { get; }

    /// <summary>
    /// The sectioned grid of this build menu, or null when sections are off.
    /// </summary>
    internal static SectionedGrid For(BuildUi ui)
    {
        return Current != null && Current.Ui == ui ? Current : null;
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
        bool hiddenView = Ui.m_pieceLists[Ui.m_currentPieceList] is ByUsagePieceList && Ui.m_currentTagId == PieceCategories.HiddenTagId;
        bool buildableOnly = MenuState.BuildableOnly;
        foreach (BuildUiPieceButton button in Ui.m_pieceButtons)
        {
            if ((!hiddenView && HiddenPieces.IsHidden(button.Piece)) || (buildableOnly && !Buildable(button.Piece)))
            {
                button.transform.SetParent(_setAside, false);
                continue;
            }

            _pieces.Add(button.Piece);
            unplaced[button.Piece] = button;
        }

        IPieceList list = Ui.m_pieceLists[Ui.m_currentPieceList];
        List<SectionSpec> specs = MenuSections.Build(list, Ui.m_currentTagId, Ui.m_currentBuildTool, _pieces, CategoriesList());
        bool headers = specs.Count > 1 || specs.Exists(spec => spec.KeepEmpty);
        string scope = list.GetType().Name + "/";

        _used = 0;
        Section section = null;
        foreach (SectionSpec spec in specs)
        {
            section = Take();
            section.Key = scope + spec.Key;
            section.TitleText = spec.Title;
            section.Header.SetActive(headers);
            section.FavoriteTag = spec.FavoriteTag;
            section.KeepEmpty = spec.KeepEmpty;
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

        foreach (BuildUiPieceButton pooled in Ui.m_pieceButtonPool)
        {
            pooled.transform.SetParent(_pool, false);
        }

        Refresh();
    }

    private ByUsagePieceList CategoriesList()
    {
        foreach (IPieceList list in Ui.m_pieceLists)
        {
            if (list is ByUsagePieceList categories)
            {
                return categories;
            }
        }

        return null;
    }

    private static bool Buildable(Piece piece)
    {
        Player player = Player.m_localPlayer;
        return player == null || player.HaveRequirements(piece, Player.RequirementMode.CanBuild);
    }

    private void Place(Section section, BuildUiPieceButton button)
    {
        if (!button.TryGetComponent(out PieceDrag _))
        {
            button.gameObject.AddComponent<PieceDrag>().Init(button, Ui.m_pieceScrollRect);
        }

        button.transform.SetParent(section.Grid, false);
        button.transform.SetAsLastSibling();
        section.Buttons.Add(button);
    }

    private BuildUiPieceButton Extra(Piece piece)
    {
        BuildUiPieceButton button = _extraPool.Count > 0
            ? _extraPool.Pop()
            : Object.Instantiate(Ui.m_pieceButtonPrefab, _pool).GetComponent<BuildUiPieceButton>();

        button.gameObject.SetActive(true);
        button.Setup(piece, Ui);
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
        if (MenuSearch.HasPrefix(term))
        {
            // The game searched names and materials; a prefixed search decides for every button.
            for (int i = 0; i < _used; i++)
            {
                foreach (BuildUiPieceButton button in _sections[i].Buttons)
                {
                    button.gameObject.SetActive(MenuSearch.Matches(button.Piece, _sections[i].TitleText, term));
                }
            }
        }
        else
        {
            foreach (BuildUiPieceButton extra in _extras)
            {
                extra.gameObject.SetActive(Matches(extra, term));
            }
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

            // A search opens every section, so no match is hidden, but sections can still be closed while it lasts.
            bool collapsed = section.Header.activeSelf && (term.Length == 0
                ? MenuState.IsCollapsed(section.Key)
                : SearchCollapsed(term).Contains(section.Key));
            section.Root.SetActive(shown > 0 || (section.KeepEmpty && term.Length == 0));
            section.Grid.gameObject.SetActive(!collapsed);
            section.Title.text = section.TitleText + "  <alpha=#99>" + shown;
            section.Arrow.localEulerAngles = new Vector3(0f, 0f, collapsed ? 90f : 0f);
        }

        Ui.ConfigureButtonNavigation();
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

    /// <summary>
    /// The favourites category of the section a button is in, or null outside the Favorites tab.
    /// </summary>
    internal int? FavoriteTagOf(BuildUiPieceButton button)
    {
        for (int i = 0; i < _used; i++)
        {
            if (_sections[i].Buttons.Contains(button))
            {
                return _sections[i].FavoriteTag;
            }
        }

        return null;
    }

    /// <summary>
    /// Where a dragged favourite lands: the section under the pointer, that section's pieces, and the
    /// piece it was dropped on (null when dropped on the header or empty space, for the end).
    /// </summary>
    internal bool TryGetDrop(PointerEventData eventData, out int tag, out List<Piece> pieces, out Piece before)
    {
        tag = FavoriteSections.Uncategorised;
        pieces = null;
        before = null;
        GameObject hit = eventData.pointerCurrentRaycast.gameObject;
        for (int i = 0; i < _used; i++)
        {
            Section section = _sections[i];
            if (section.FavoriteTag == null || !section.Root.activeInHierarchy)
            {
                continue;
            }

            BuildUiPieceButton onto = hit != null ? section.Buttons.Find(button => hit.transform.IsChildOf(button.transform)) : null;
            bool over = onto != null
                || RectTransformUtility.RectangleContainsScreenPoint((RectTransform)section.Root.transform, eventData.position, eventData.pressEventCamera);
            if (!over)
            {
                continue;
            }

            tag = section.FavoriteTag.Value;
            pieces = section.Buttons.ConvertAll(button => button.Piece);
            before = onto != null ? onto.Piece : null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lays the sections out again after favourites were moved.
    /// </summary>
    internal void Rearranged()
    {
        Rebuild();
    }

    /// <summary>
    /// Shows how many pieces each entry in the list on the left has, after its name.
    /// </summary>
    internal void ShowCounts()
    {
        IPieceList list = Ui.m_pieceLists[Ui.m_currentPieceList];
        PieceTable table = Ui.m_currentBuildTool;
        if (!list.ShowTags || table == null || !Ui.m_tagListContainer.gameObject.activeSelf)
        {
            return;
        }

        foreach (BuildUiTagButton button in Ui.m_tagButtons)
        {
            ShowCount(button, Count(list, button.m_tagId, table));
        }

        ShowCount(Ui.m_showAllTagsButton, Count(list, -1, table));
    }

    private int Count(IPieceList list, int tagId, PieceTable table)
    {
        _counted.Clear();
        list.GetAvailablePiecesWithTag(tagId, table, _counted);
        int count = 0;
        foreach (Piece piece in _counted)
        {
            if (piece != null && !PieceCategories.IsSpecial(piece) && (tagId == PieceCategories.HiddenTagId || !HiddenPieces.IsHidden(piece)))
            {
                count++;
            }
        }

        _counted.Clear();
        return count;
    }

    private static void ShowCount(BuildUiTagButton button, int count)
    {
        string text = Localization.instance.Localize(button.m_displayName) + "  <alpha=#88>" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (TextMeshProUGUI mesh in button.m_textMeshes)
        {
            mesh.richText = true;
            mesh.text = text;
        }
    }

    /// <summary>
    /// Lets the player's favourites categories in the list on the left be dragged and renamed.
    /// </summary>
    internal void HookTagButtons()
    {
        foreach (BuildUiTagButton button in Ui.m_tagButtons)
        {
            if (!button.TryGetComponent(out TagDrag _))
            {
                button.gameObject.AddComponent<TagDrag>().Init(Ui, button, Ui.m_tagListScrollRect);
            }
        }
    }

    private void Toggle(Section section)
    {
        string term = SearchTerm();
        if (term.Length > 0)
        {
            HashSet<string> closed = SearchCollapsed(term);
            if (!closed.Remove(section.Key))
            {
                _ = closed.Add(section.Key);
            }
        }
        else
        {
            MenuState.SetCollapsed(section.Key, !MenuState.IsCollapsed(section.Key));
        }

        Refresh();
    }

    /// <summary>
    /// The sections closed during the current search. They open again when the search changes, and the
    /// saved open or closed state is left as it was.
    /// </summary>
    private HashSet<string> SearchCollapsed(string term)
    {
        if (term != _collapsedFor)
        {
            _collapsedFor = term;
            _searchCollapsed.Clear();
        }

        return _searchCollapsed;
    }

    private readonly HashSet<string> _searchCollapsed = [];
    private string _collapsedFor = "";

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
        section.Layout.cellSize = _template.cellSize * BuildMenuConfig.IconScale;
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

        // TextMeshPro is added while the object is inactive so its Awake runs after the font is set. Otherwise
        // it looks for its default font (LiberationSans SDF), which Valheim doesn't ship, and logs a warning.
        GameObject title = new("Title", typeof(RectTransform));
        title.SetActive(false);
        RectTransform titleRect = (RectTransform)title.transform;
        titleRect.SetParent(header.transform, false);
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(ArrowSize + 12f, 2f);
        titleRect.offsetMax = Vector2.zero;
        section.Title = title.AddComponent<TextMeshProUGUI>();
        if (_fontSource != null)
        {
            section.Title.font = _fontSource.font;
            section.Title.fontSharedMaterial = _fontSource.fontSharedMaterial;
            section.Title.fontSize = _fontSource.fontSize;
        }

        title.SetActive(true);

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

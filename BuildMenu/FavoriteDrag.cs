using TMPro;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Drags a favourite piece to another place in its section, or into another section. Anywhere else (other
/// tabs, or Favorites grouped or sorted another way), the drag goes to the scroll view as usual.
/// </summary>
internal sealed class PieceDrag : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private BuildUiPieceButton _button;
    private ScrollRect _scroll;
    private DragGhost _ghost;
    private bool _forwarding;

    internal void Init(BuildUiPieceButton button, ScrollRect scroll)
    {
        _button = button;
        _scroll = scroll;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        _scroll.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        SectionedGrid grid = SectionedGrid.Current;
        _forwarding = eventData.button != PointerEventData.InputButton.Left
            || grid == null
            || !FavoriteSections.CanArrange(grid.Ui)
            || grid.FavoriteTagOf(_button) == null;
        if (_forwarding)
        {
            _scroll.OnBeginDrag(eventData);
            return;
        }

        _ghost = DragGhost.ForIcon(_button.m_icon, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_forwarding)
        {
            _scroll.OnDrag(eventData);
        }
        else
        {
            _ghost?.Follow(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_forwarding)
        {
            _scroll.OnEndDrag(eventData);
            return;
        }

        _ghost?.Destroy();
        _ghost = null;
        SectionedGrid grid = SectionedGrid.Current;
        if (grid != null && grid.FavoriteTagOf(_button) is int from
            && grid.TryGetDrop(eventData, out int to, out System.Collections.Generic.List<Piece> pieces, out Piece before))
        {
            FavoriteSections.MovePiece(grid.Ui, _button.Piece, from, to, pieces, before);
            grid.Rearranged();
        }
    }
}

/// <summary>
/// Drags one of the player's favourites categories in the list on the left to another place. Elsewhere
/// the drag goes to the list's scroll view. Right-clicking such a category renames it.
/// </summary>
internal sealed class TagDrag : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private BuildUi _ui;
    private BuildUiTagButton _button;
    private ScrollRect _scroll;
    private DragGhost _ghost;
    private bool _forwarding;

    internal void Init(BuildUi ui, BuildUiTagButton button, ScrollRect scroll)
    {
        _ui = ui;
        _button = button;
        _scroll = scroll;
        UIInputHandler input = button.GetComponent<UIInputHandler>();
        if (input != null)
        {
            input.m_onRightClick += _ =>
            {
                if (IsFavoriteCategory())
                {
                    FavoriteSections.OpenRename(_ui, _button.m_tagId);
                }
            };
        }
    }

    private bool IsFavoriteCategory()
    {
        return IsRenameTarget(_ui, _button);
    }

    /// <summary>
    /// Whether the button is one of the player's favourites categories, which can be renamed and moved.
    /// </summary>
    internal static bool IsRenameTarget(BuildUi ui, BuildUiTagButton button)
    {
        return button != null && ui.m_pieceLists[ui.m_currentPieceList] is FavoritePieceList && button.m_canDeleteTag && button.m_tagId >= 0;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        _scroll.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _forwarding = eventData.button != PointerEventData.InputButton.Left || !IsFavoriteCategory();
        if (_forwarding)
        {
            _scroll.OnBeginDrag(eventData);
            return;
        }

        _ghost = DragGhost.ForText(_button.CurrentText, _button.GetComponentInChildren<TextMeshProUGUI>(), eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_forwarding)
        {
            _scroll.OnDrag(eventData);
        }
        else
        {
            _ghost?.Follow(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_forwarding)
        {
            _scroll.OnEndDrag(eventData);
            return;
        }

        _ghost?.Destroy();
        _ghost = null;
        GameObject hit = eventData.pointerCurrentRaycast.gameObject;
        BuildUiTagButton target = hit != null ? hit.GetComponentInParent<BuildUiTagButton>() : null;
        if (target != null && target != _button && target.m_canDeleteTag && target.m_tagId >= 0)
        {
            FavoriteSections.MoveCategory(_ui, _button.m_tagId, target.m_tagId);
        }
    }
}

/// <summary>
/// A see-through copy of what's being dragged, following the pointer. It doesn't block the pointer, so
/// the drop lands on what's underneath.
/// </summary>
internal sealed class DragGhost
{
    private const float Alpha = 0.7f;

    private readonly RectTransform _rect;
    private readonly RectTransform _canvas;

    private DragGhost(RectTransform rect, RectTransform canvas)
    {
        _rect = rect;
        _canvas = canvas;
    }

    internal static DragGhost ForIcon(Image icon, PointerEventData eventData)
    {
        DragGhost ghost = Create(icon, out GameObject root);
        Image image = root.AddComponent<Image>();
        image.sprite = icon.sprite;
        image.preserveAspect = true;
        image.color = new Color(1f, 1f, 1f, Alpha);
        image.raycastTarget = false;
        ghost._rect.sizeDelta = ((RectTransform)icon.transform).rect.size;
        ghost.Follow(eventData);
        return ghost;
    }

    internal static DragGhost ForText(string text, TextMeshProUGUI source, PointerEventData eventData)
    {
        DragGhost ghost = Create(source, out GameObject root);

        // Added while inactive so TextMeshPro's Awake runs after the font is set, not looking for LiberationSans SDF.
        root.SetActive(false);
        TextMeshProUGUI label = root.AddComponent<TextMeshProUGUI>();
        if (source != null)
        {
            label.font = source.font;
            label.fontSharedMaterial = source.fontSharedMaterial;
            label.fontSize = source.fontSize;
        }

        root.SetActive(true);

        label.text = text;
        label.color = new Color(SectionedGrid.TitleColor.r, SectionedGrid.TitleColor.g, SectionedGrid.TitleColor.b, Alpha);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        ghost._rect.sizeDelta = new Vector2(200f, 30f);
        ghost.Follow(eventData);
        return ghost;
    }

    private static DragGhost Create(Component near, out GameObject root)
    {
        Canvas canvas = near != null ? near.GetComponentInParent<Canvas>() : null;
        RectTransform canvasRect = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
        root = new GameObject("BetterHammer_DragGhost", typeof(RectTransform));
        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(canvasRect, false);
        rect.SetAsLastSibling();
        return new DragGhost(rect, canvasRect);
    }

    internal void Follow(PointerEventData eventData)
    {
        if (_rect != null && _canvas != null
            && RectTransformUtility.ScreenPointToWorldPointInRectangle(_canvas, eventData.position, eventData.pressEventCamera, out Vector3 world))
        {
            _rect.position = world;
        }
    }

    internal void Destroy()
    {
        if (_rect != null)
        {
            Object.Destroy(_rect.gameObject);
        }
    }
}

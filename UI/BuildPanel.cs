using System.Collections.Generic;

using TMPro;

using UnityEngine;

namespace BetterHammer.UI;

/// <summary>
/// Gives the build panel (the selected piece's name, description, materials and station at the bottom of
/// the screen) room for up to <see cref="MaxSlots"/> material and station slots instead of the game's six.
/// The panel is only as wide as it needs to be: the game's width for a single piece, wider two slots at a
/// time for a copy or selection that needs more. It's also raised just enough to clear the row of key
/// hints above the vanilla ones.
/// </summary>
internal static class BuildPanel
{
    internal const int MaxSlots = 10;

    /// <summary>
    /// How many slots the panel grows by at a time, so it grows by the same amount on either side.
    /// </summary>
    private const int SlotGrowth = 2;

    /// <summary>
    /// Space between the slots' left edges, as the game spaces its own.
    /// </summary>
    private const float SlotStep = 66f;

    /// <summary>
    /// Room kept under the panel for one row of key hints (a key is 32 high) above the vanilla row.
    /// </summary>
    private const float HintRowHeight = 32f;
    private const float HintRowGap = 12f;

    /// <summary>
    /// The smallest the description is shrunk to fit; below this it's cut short instead.
    /// </summary>
    private const float DescriptionMinFontSize = 12f;

    private static Hud _setUp;
    private static RectTransform _panel;
    private static RectTransform _requirements;
    private static RectTransform _top;
    private static RectTransform _buildIcon;
    private static RectTransform _snappingIcon;
    private static Vector2 _panelSize;
    private static Vector2 _requirementsSize;
    private static Vector2 _topSize;
    private static Vector2 _buildIconPosition;
    private static Vector2 _snappingIconPosition;
    private static float _baseY;
    private static int _width = -1;

    /// <summary>
    /// How many slots the game's panel has.
    /// </summary>
    internal static int BaseSlots { get; private set; } = 6;

    /// <summary>
    /// How many slots the panel can show at most (fewer if it couldn't be widened).
    /// </summary>
    internal static int AvailableSlots => _panel != null ? MaxSlots : BaseSlots;

    /// <summary>
    /// The panel width, in slots, to show the given number of materials and stations: the game's width,
    /// or wider two slots at a time, up to <see cref="AvailableSlots"/>.
    /// </summary>
    internal static int WidthFor(int needed)
    {
        int width = BaseSlots;
        while (width < needed && width < AvailableSlots)
        {
            width = Mathf.Min(width + SlotGrowth, AvailableSlots);
        }

        return width;
    }

    /// <summary>
    /// Runs before Hud.SetupPieceInfo, which the game calls every frame while building: adds the extra
    /// slots the first time, and keeps the panel clear of the key hints.
    /// </summary>
    internal static void BeforeSetupPieceInfo(Hud hud)
    {
        if (_setUp != hud)
        {
            _setUp = hud;
            _width = -1;
            _panel = AddSlots(hud);
            _baseY = _panel != null ? _panel.anchoredPosition.y : 0f;
        }

        if (_panel != null)
        {
            KeepClearOfHints();
        }
    }

    /// <summary>
    /// Adds hidden slots after the game's own, up to <see cref="MaxSlots"/>. Returns the panel, or null if
    /// the game's layout isn't as expected (the panel is then left as it is).
    /// </summary>
    private static RectTransform AddSlots(Hud hud)
    {
        GameObject[] items = hud.m_requirementItems;
        BaseSlots = items.Length;
        _requirements = items.Length > 0 ? items[0].transform.parent as RectTransform : null;
        RectTransform panel = _requirements != null ? _requirements.parent as RectTransform : null;
        _top = panel != null ? panel.Find("selected_piece") as RectTransform : null;
        if (_top == null || hud.m_buildIcon == null || items.Length >= MaxSlots)
        {
            Debug.LogWarning("BetterHammer: build panel layout not as expected, leaving it as it is.");
            return null;
        }

        List<GameObject> slots = [.. items];
        GameObject template = items[items.Length - 1];
        RectTransform last = (RectTransform)template.transform;
        for (int i = 1; i <= MaxSlots - items.Length; i++)
        {
            GameObject slot = Object.Instantiate(template, _requirements, false);
            slot.name = "BetterHammer_res_bkg (" + (items.Length + i - 1) + ")";
            slot.SetActive(false);
            ((RectTransform)slot.transform).anchoredPosition = last.anchoredPosition + new Vector2(SlotStep * i, 0f);
            slots.Add(slot);
        }

        hud.m_requirementItems = [.. slots];

        _buildIcon = (RectTransform)hud.m_buildIcon.transform;
        _snappingIcon = hud.m_snappingIcon != null ? (RectTransform)hud.m_snappingIcon.transform : null;
        _panelSize = panel.sizeDelta;
        _requirementsSize = _requirements.sizeDelta;
        _topSize = _top.sizeDelta;
        _buildIconPosition = _buildIcon.anchoredPosition;
        _snappingIconPosition = _snappingIcon != null ? _snappingIcon.anchoredPosition : Vector2.zero;

        if (hud.m_pieceDescription is TMP_Text description)
        {
            description.fontSizeMin = Mathf.Min(DescriptionMinFontSize, description.fontSizeMax);
        }

        return panel;
    }

    /// <summary>
    /// Makes the panel wide enough for the given number of slots, growing on both sides of the centre
    /// with the piece icon and the snapping icon staying at the edges. Only changes it when that changes.
    /// </summary>
    internal static void SetWidth(int slots)
    {
        if (_panel == null)
        {
            return;
        }

        slots = Mathf.Clamp(slots, BaseSlots, MaxSlots);
        if (slots == _width)
        {
            return;
        }

        _width = slots;
        float added = SlotStep * (slots - BaseSlots);
        Vector2 wider = new(added, 0f);
        _panel.sizeDelta = _panelSize + wider;
        _requirements.sizeDelta = _requirementsSize + wider;
        _top.sizeDelta = _topSize + wider;
        _buildIcon.anchoredPosition = _buildIconPosition - new Vector2(added / 2f, 0f);
        if (_snappingIcon != null)
        {
            _snappingIcon.anchoredPosition = _snappingIconPosition + new Vector2(added / 2f, 0f);
        }
    }

    /// <summary>
    /// Keeps the panel where the game has it, or raised just enough for its bottom to clear a row of
    /// key hints on top of the vanilla ones. Only moves it when that changes.
    /// </summary>
    private static void KeepClearOfHints()
    {
        Transform row = KeyHints.instance != null && KeyHints.instance.m_buildHints != null
            ? KeyHints.instance.m_buildHints.transform.Find("Keyboard")
            : null;
        float scale = _panel.lossyScale.y;
        if (row is not RectTransform rowRect || scale <= 0f)
        {
            return;
        }

        Vector3[] corners = new Vector3[4];
        rowRect.GetWorldCorners(corners);
        float clear = corners[1].y + ((HintRowHeight + HintRowGap) * scale);

        _panel.GetWorldCorners(corners);
        float baseBottom = corners[0].y - ((_panel.anchoredPosition.y - _baseY) * scale);
        float y = _baseY + Mathf.Max(0f, (clear - baseBottom) / scale);
        if (Mathf.Abs(_panel.anchoredPosition.y - y) > 0.01f)
        {
            _panel.anchoredPosition = new Vector2(_panel.anchoredPosition.x, y);
        }
    }
}

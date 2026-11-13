using System.Collections.Generic;

using TMPro;

using UnityEngine;

namespace BetterHammer.UI;

/// <summary>
/// Makes the build panel (the selected piece's name, description, materials and station at the bottom of
/// the screen) wider, with <see cref="Slots"/> material and station slots instead of the game's six, so a
/// copy needing many materials and stations shows more of them at once. The panel is also raised just
/// enough to clear the row of key hints above the vanilla ones.
/// </summary>
internal static class BuildPanel
{
    internal const int Slots = 10;

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
    private static float _baseY;

    /// <summary>
    /// Runs before Hud.SetupPieceInfo, which the game calls every frame while building: widens the panel
    /// the first time, and keeps it clear of the key hints.
    /// </summary>
    internal static void BeforeSetupPieceInfo(Hud hud)
    {
        if (_setUp != hud)
        {
            _setUp = hud;
            _panel = Widen(hud);
            _baseY = _panel != null ? _panel.anchoredPosition.y : 0f;
        }

        if (_panel != null)
        {
            KeepClearOfHints();
        }
    }

    /// <summary>
    /// Adds slots and widens the panel to fit them. Returns the panel, or null if the game's layout isn't
    /// as expected (the panel is then left as it is).
    /// </summary>
    private static RectTransform Widen(Hud hud)
    {
        GameObject[] items = hud.m_requirementItems;
        RectTransform requirements = items.Length > 0 ? items[0].transform.parent as RectTransform : null;
        RectTransform panel = requirements != null ? requirements.parent as RectTransform : null;
        RectTransform top = panel != null ? panel.Find("selected_piece") as RectTransform : null;
        if (top == null || hud.m_buildIcon == null)
        {
            Debug.LogWarning("BetterHammer: build panel layout not as expected, leaving it as it is.");
            return null;
        }

        int extra = Slots - items.Length;
        if (extra > 0)
        {
            List<GameObject> slots = [.. items];
            GameObject template = items[items.Length - 1];
            RectTransform last = (RectTransform)template.transform;
            for (int i = 1; i <= extra; i++)
            {
                GameObject slot = Object.Instantiate(template, requirements, false);
                slot.name = "BetterHammer_res_bkg (" + (items.Length + i - 1) + ")";
                slot.SetActive(false);
                ((RectTransform)slot.transform).anchoredPosition = last.anchoredPosition + new Vector2(SlotStep * i, 0f);
                slots.Add(slot);
            }

            hud.m_requirementItems = [.. slots];

            // Wider by the added slots on both sides of the centre, the piece icon and the snapping icon
            // staying at the edges.
            float added = SlotStep * extra;
            Vector2 wider = new(added, 0f);
            panel.sizeDelta += wider;
            requirements.sizeDelta += wider;
            top.sizeDelta += wider;
            ((RectTransform)hud.m_buildIcon.transform).anchoredPosition -= new Vector2(added / 2f, 0f);
            if (hud.m_snappingIcon != null)
            {
                ((RectTransform)hud.m_snappingIcon.transform).anchoredPosition += new Vector2(added / 2f, 0f);
            }
        }

        if (hud.m_pieceDescription is TMP_Text description)
        {
            description.fontSizeMin = Mathf.Min(DescriptionMinFontSize, description.fontSizeMax);
        }

        return panel;
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

using BetterHammer.UI;

using UnityEngine;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Makes the build menu taller: up into the empty space above it, and down by as much as the build panel
/// was lowered (see <see cref="BuildPanel.Drop"/>), so the gap between them stays the game's. Worked out
/// again each time the menu opens, in case the screen or GUI scale changed.
/// </summary>
internal static class MenuLayout
{
    /// <summary>
    /// How much taller the menu grows upwards, at most.
    /// </summary>
    private const float ExtraUp = 120f;

    /// <summary>
    /// Space kept between the top of the menu and the top of the screen.
    /// </summary>
    private const float TopMargin = 40f;

    private static RectTransform _bar;
    private static Vector2 _size;
    private static Vector2 _position;

    internal static void Apply(BuildUi ui)
    {
        if (_bar == null)
        {
            _bar = ui.transform.Find("bar") as RectTransform;
            if (_bar == null)
            {
                Debug.LogWarning("BetterHammer: build menu layout not as expected, leaving its size as it is.");
                return;
            }

            _size = _bar.sizeDelta;
            _position = _bar.anchoredPosition;
        }

        _bar.sizeDelta = _size;
        _bar.anchoredPosition = _position;

        float up = Mathf.Min(ExtraUp, Mathf.Max(0f, SpaceAbove() - TopMargin));
        float down = BuildPanel.Drop();
        _bar.sizeDelta = _size + new Vector2(0f, up + down);
        _bar.anchoredPosition = _position + new Vector2(0f, (up - down) / 2f);
    }

    /// <summary>
    /// The space between the top of the menu, at the game's size, and the top of the screen.
    /// </summary>
    private static float SpaceAbove()
    {
        Canvas canvas = _bar.GetComponentInParent<Canvas>();
        float scale = _bar.lossyScale.y;
        if (canvas == null || scale <= 0f)
        {
            return 0f;
        }

        Vector3[] corners = new Vector3[4];
        ((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(corners);
        float screenTop = corners[1].y;
        _bar.GetWorldCorners(corners);
        return (screenTop - corners[1].y) / scale;
    }
}

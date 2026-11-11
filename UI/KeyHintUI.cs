using System.Collections.Generic;

using BetterHammer.Structures;

using HarmonyLib;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace BetterHammer.UI;

/// <summary>
/// Adds this mod's controls to the vanilla build key hints (bottom of the screen while placing
/// pieces). Each hint is built from clones of the vanilla hint pieces (label, boxed key, "+" and
/// the scroll icon), stacked in two columns right-aligned just above the vanilla hints, and is only
/// shown while a hammer the mod patches is equipped.
/// </summary>
internal static class KeyHintUI
{
    private const string ContainerName = "BetterHammer_KeyHints";
    private const string ScrollIcon = "@scroll";

    private const float LabelFontSize = 16f;
    /// <summary>
    /// layout height of the icon: matches a boxed key
    /// </summary>
    private const float IconHeight = 32f;
    /// <summary>
    /// drawn this much bigger than its layout size: the sprite has padding
    /// </summary>
    private const float IconScale = 1.8f;
    private const float ColumnSpacing = 12f;
    /// <summary>
    /// room for the scaled-up icons, which are drawn taller than their layout height
    /// </summary>
    private const float RowSpacing = 16f;

    private enum HintVisibility { Always = 0, GridOnly = 1, BuildOnly = 2, SelectionOnly = 3, CopyOnly = 4 }

    /// <summary>
    /// In a hint's keys, shown as "/" between two alternatives instead of the usual "+".
    /// </summary>
    private const string Or = "/";

    /// <summary>
    /// In a hint's keys, the configured delete and clear selection keys.
    /// </summary>
    private const string DeleteKey = "@delete";
    private const string ClearKey = "@clear";

    private sealed class Hint
    {
        public string Label;
        public int Column;
        /// <summary>
        /// localisation tokens or literal key names; ScrollIcon draws the scroll wheel icon and Or puts a
        /// "/" between the keys either side of it
        /// </summary>
        public string[] Keys;
        public HintVisibility Visibility;
        public GameObject Item;
        public CanvasGroup Group;
    }

    /// <summary>
    /// Two columns, each stacked top to bottom; the right column sits above the vanilla "Rotate" hint.
    /// </summary>
    private static readonly List<Hint> Hints =
    [
        new Hint { Label = "$betterhammer_hint_copy_selection", Column = 0, Keys = ["Left Alt", "$button_mouse0"], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_deselect", Column = 0, Keys = ["Left Alt", "$button_mouse1"], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_deselect_connected", Column = 0, Keys = ["Left Alt", "Left Shift", "$button_mouse1"], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_select", Column = 0, Keys = ["Left Alt", "$button_mouse2"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_select_connected", Column = 0, Keys = ["Left Alt", "Left Shift", "$button_mouse2"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_move", Column = 0, Keys = ["Left Ctrl", "$button_mouse2"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_grid", Column = 0, Keys = ["Left Ctrl", "$button_mouse1"], Visibility = HintVisibility.BuildOnly },
        new Hint { Label = "$betterhammer_hint_repair", Column = 0, Keys = ["Left Shift", "$button_mouse1"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_copy_piece", Column = 1, Keys = ["Left Shift", "$KEY_TabLeft", Or, "$KEY_TabRight"], Visibility = HintVisibility.CopyOnly },
        new Hint { Label = "$betterhammer_hint_delete_selection", Column = 1, Keys = [DeleteKey], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_clear_selection", Column = 1, Keys = [ClearKey], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_grid_distance", Column = 1, Keys = ["Left Ctrl", ScrollIcon], Visibility = HintVisibility.GridOnly },
        new Hint { Label = "$betterhammer_hint_zoom", Column = 1, Keys = ["Left Alt", ScrollIcon], Visibility = HintVisibility.Always },
    ];

    private static GameObject _container;
    private static RectTransform _rotateAnchor;
    private static RectTransform _rotateIcon;
    private static Image _rotateIconImage;
    private static bool _createFailed;
    private static bool _layoutDirty;

    /// <summary>
    /// The vanilla right-click ("Build Menu") hint's label, and its text before it was changed to "Abort".
    /// </summary>
    private static TextMeshProUGUI _buildMenuLabel;
    private static string _buildMenuText;

    public static void Update(KeyHints keyHints, Player player)
    {
        UpdateAbortHint(keyHints);

        bool show = keyHints.m_buildHints != null
            && keyHints.m_buildHints.activeInHierarchy
            && player != null
            && player.InPlaceMode()
            && !ZInput.IsGamepadActive()
            && BetterHammerPlugin.IsHammerEquipped(player);

        if (!show)
        {
            if (_container != null && _container.activeSelf)
            {
                _container.SetActive(false);
            }

            return;
        }

        if (_container == null && (_createFailed || !Create(keyHints)))
        {
            return;
        }

        bool repairing = player.InRepairMode();
        bool gridOn = BetterHammerPlugin.GridEnabled;

        foreach (Hint hint in Hints)
        {
            bool visible = true;
            bool blank = false; // keeps its space so the other column's rows stay level
            switch (hint.Visibility)
            {
                case HintVisibility.BuildOnly:
                    visible = !repairing;
                    break;
                case HintVisibility.GridOnly:
                    visible = !repairing;
                    blank = !gridOn;
                    break;
                case HintVisibility.SelectionOnly:
                    blank = !MultiCopy.HasSelection;
                    break;
                case HintVisibility.CopyOnly:
                    visible = GhostGroup.Active;
                    break;
            }

            if (hint.Item.activeSelf != visible)
            {
                hint.Item.SetActive(visible);
                _layoutDirty = true;
            }
            hint.Group.alpha = blank ? 0f : 1f;
        }

        if (!_container.activeSelf)
        {
            _container.SetActive(true);
            _layoutDirty = true;
        }

        if (_layoutDirty)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_container.transform);
            _layoutDirty = false;
        }

        AlignToRotateHint();
    }

    /// <summary>
    /// While moving a piece, right-click aborts the move instead of opening the build menu, so its
    /// vanilla hint says so. The vanilla text is put back afterwards.
    /// </summary>
    private static void UpdateAbortHint(KeyHints keyHints)
    {
        if (_buildMenuLabel == null && keyHints.m_buildHints != null)
        {
            Transform label = keyHints.m_buildHints.transform.Find("Keyboard/BuildMenu/Text");
            _buildMenuLabel = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
            _buildMenuText = null;
        }

        if (_buildMenuLabel == null)
        {
            return;
        }

        if (PieceMover.Moving)
        {
            _buildMenuText ??= _buildMenuLabel.text;
            _buildMenuLabel.text = Localization.instance.Localize("$betterhammer_hint_abort");
        }
        else if (_buildMenuText != null)
        {
            _buildMenuLabel.text = _buildMenuText;
            _buildMenuText = null;
        }
    }

    /// <summary>
    /// The container's top right lines up with the vanilla "Rotate" hint: its top, and the right edge of
    /// its icon as drawn (the icon's rect is wider than the sprite inside it).
    /// </summary>
    private static void AlignToRotateHint()
    {
        Vector3[] corners = new Vector3[4];
        _rotateAnchor.GetWorldCorners(corners); // 0 = bottom left, 1 = top left, 2 = top right, 3 = bottom right

        Rect iconRect = _rotateIcon.rect;
        float drawnWidth = Mathf.Min(iconRect.width, iconRect.height * SpriteAspect(_rotateIconImage));
        Vector3 iconCentre = _rotateIcon.TransformPoint(iconRect.center);
        float right = iconCentre.x + (drawnWidth / 2f * _rotateIcon.lossyScale.x);

        _container.transform.position = new Vector3(right, corners[2].y, corners[2].z);
    }

    private static bool Create(KeyHints keyHints)
    {
        Transform keyboard = keyHints.m_buildHints.transform.Find("Keyboard");
        Transform copy = keyboard != null ? keyboard.Find("Copy") : null;
        Transform rotate = keyboard != null ? keyboard.Find("rotate") : null;
        Transform labelTemplate = copy != null ? copy.Find("Text") : null;
        Transform plusTemplate = copy != null ? copy.Find("Text (1)") : null;
        Transform keyTemplate = copy != null ? copy.Find("key_bkg") : null;
        Transform iconTemplate = rotate != null ? rotate.Find("mousew_icon") : null;
        HorizontalLayoutGroup keyboardLayout = keyboard != null ? keyboard.GetComponent<HorizontalLayoutGroup>() : null;

        if (labelTemplate == null || plusTemplate == null || keyTemplate == null || iconTemplate == null || keyboardLayout == null)
        {
            _createFailed = true;
            Debug.LogWarning("BetterHammer: vanilla build key hints layout not as expected, not adding key hints.");
            return false;
        }

        RectTransform rect = new GameObject(ContainerName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(keyHints.m_buildHints.transform, false);
        rect.pivot = new Vector2(1f, 0f);

        HorizontalLayoutGroup rowLayout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = ColumnSpacing;
        rowLayout.childAlignment = TextAnchor.LowerRight;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        int columnCount = 0;
        foreach (Hint hint in Hints)
        {
            columnCount = Mathf.Max(columnCount, hint.Column + 1);
        }

        Transform[] columns = new Transform[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            GameObject column = new(ContainerName + "_Column" + c, typeof(RectTransform));
            column.transform.SetParent(rect, false);
            VerticalLayoutGroup columnLayout = column.AddComponent<VerticalLayoutGroup>();
            columnLayout.spacing = RowSpacing;
            // The right column is right-aligned so its icons stack above the vanilla Rotate icon.
            columnLayout.childAlignment = c == columnCount - 1 ? TextAnchor.LowerRight : TextAnchor.LowerLeft;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;
            columnLayout.childForceExpandWidth = false;
            columnLayout.childForceExpandHeight = false;
            columns[c] = column.transform;
        }

        foreach (Hint hint in Hints)
        {
            // Start from a vanilla multi-key hint (for its row settings), then rebuild its contents.
            GameObject item = Object.Instantiate(copy.gameObject, columns[hint.Column], false);
            item.name = "BetterHammer_Hint_" + hint.Label.TrimStart('$');
            item.SetActive(true);
            for (int i = item.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(item.transform.GetChild(i).gameObject);
            }

            AddText(labelTemplate, item.transform, Localization.instance.Localize(hint.Label));

            for (int i = 0; i < hint.Keys.Length; i++)
            {
                if (hint.Keys[i] == Or)
                {
                    AddText(plusTemplate, item.transform, Or);
                    continue;
                }

                if (i > 0 && hint.Keys[i - 1] != Or)
                {
                    AddText(plusTemplate, item.transform, "+");
                }

                if (hint.Keys[i] == ScrollIcon)
                {
                    GameObject icon = Object.Instantiate(iconTemplate.gameObject, item.transform, false);
                    icon.SetActive(true);

                    // Without this the layout sizes the icon from its sprite's native size. Its layout
                    // size is that of a key box, with a width that keeps the sprite's aspect ratio. The
                    // sprite is padded, so it is drawn bigger than that (scaling doesn't affect layout).
                    Image image = icon.GetComponent<Image>();
                    image.preserveAspect = true;
                    float aspect = SpriteAspect(image);
                    LayoutElement iconLayout = icon.GetComponent<LayoutElement>() ?? icon.AddComponent<LayoutElement>();
                    iconLayout.minWidth = iconLayout.preferredWidth = IconHeight * aspect * IconScale;
                    iconLayout.minHeight = iconLayout.preferredHeight = IconHeight;
                    iconLayout.flexibleWidth = iconLayout.flexibleHeight = 0f;
                    icon.transform.localScale = new Vector3(IconScale, IconScale, 1f);
                }
                else
                {
                    GameObject key = Object.Instantiate(keyTemplate.gameObject, item.transform, false);
                    key.SetActive(true);
                    key.GetComponentInChildren<TextMeshProUGUI>().text = KeyText(hint.Keys[i]);
                }
            }

            hint.Item = item;
            hint.Group = item.GetComponent<CanvasGroup>() ?? item.AddComponent<CanvasGroup>();
        }

        // Give matching parts (label, key, "+", key or icon) of a column's hints the same width, so the
        // keys line up between its rows. All items are active here, so they have a layout to measure.
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        for (int c = 0; c < columnCount; c++)
        {
            EqualiseColumnWidths(c);
        }

        _rotateAnchor = (RectTransform)rotate;
        _rotateIcon = (RectTransform)iconTemplate;
        _rotateIconImage = iconTemplate.GetComponent<Image>();
        _container = rect.gameObject;
        _layoutDirty = true;
        return true;
    }

    private static void EqualiseColumnWidths(int column)
    {
        List<Transform> items = [];
        foreach (Hint hint in Hints)
        {
            if (hint.Column == column)
            {
                items.Add(hint.Item.transform);
            }
        }

        int parts = int.MaxValue;
        foreach (Transform item in items)
        {
            parts = Mathf.Min(parts, item.childCount);
        }

        for (int part = 0; part < parts; part++)
        {
            float width = 0f;
            foreach (Transform item in items)
            {
                width = Mathf.Max(width, ((RectTransform)item.GetChild(part)).rect.width);
            }

            foreach (Transform item in items)
            {
                GameObject child = item.GetChild(part).gameObject;
                LayoutElement layout = child.GetComponent<LayoutElement>() ?? child.AddComponent<LayoutElement>();
                layout.minWidth = width;
            }
        }
    }

    private static string KeyText(string key)
    {
        return key switch
        {
            DeleteKey => CopyLibrary.DeleteKey.ToString(),
            ClearKey => MultiCopy.ClearKey.ToString(),
            _ => Localization.instance.Localize(key),
        };
    }

    private static float SpriteAspect(Image image)
    {
        return image.sprite != null ? image.sprite.rect.width / image.sprite.rect.height : 0.6f;
    }

    private static void AddText(Transform template, Transform parent, string text)
    {
        GameObject clone = Object.Instantiate(template.gameObject, parent, false);
        clone.SetActive(true);

        // Vanilla labels can have a fixed width sized for their own text.
        LayoutElement fixedSize = clone.GetComponent<LayoutElement>();
        if (fixedSize != null)
        {
            Object.DestroyImmediate(fixedSize);
        }

        TextMeshProUGUI label = clone.GetComponent<TextMeshProUGUI>();
        label.enableAutoSizing = false;
        label.fontSize = LabelFontSize;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = text;
    }

    /// <summary>
    /// The hints live under the HUD, which is rebuilt with each world load.
    /// </summary>
    public static void Reset()
    {
        _container = null;
        _rotateAnchor = null;
        _rotateIcon = null;
        _rotateIconImage = null;
        _createFailed = false;
        _buildMenuLabel = null;
        _buildMenuText = null;
        foreach (Hint hint in Hints)
        {
            hint.Item = null;
            hint.Group = null;
        }
    }

    [HarmonyPatch(typeof(KeyHints), "UpdateHints")]
    private static class KeyHints_UpdateHints_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(KeyHints __instance)
        {
            // Drop stale references if the HUD (and our container with it) was destroyed.
            if (!ReferenceEquals(_container, null) && _container == null)
            {
                Reset();
            }

            Update(__instance, Player.m_localPlayer);
        }
    }
}

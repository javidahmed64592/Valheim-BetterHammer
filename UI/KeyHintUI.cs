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
/// the scroll icon), and is only shown while a hammer the mod patches is equipped. They stand in two
/// stacks just above the vanilla hints: selecting, copying and moving on the left, lined up with the
/// vanilla "Place" hint, and building aids on the right, lined up with the vanilla "Rotate" hint. A hint
/// that doesn't apply at the moment is hidden but keeps its space, so the others never move.
/// </summary>
internal static class KeyHintUI
{
    private const string ContainerName = "BetterHammer_KeyHints";

    private enum Side { Left = 0, Right = 1 }
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
        public Side Side;
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
    /// Each side is stacked top to bottom. The left side's wider rows go at the top, keeping its lower
    /// rows clear of the selected piece's info panel in the middle of the screen.
    /// </summary>
    private static readonly List<Hint> Hints =
    [
        new Hint { Label = "$betterhammer_hint_select_connected", Side = Side.Left, Keys = ["Left Alt", "Left Shift", "$button_mouse2"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_deselect_connected", Side = Side.Left, Keys = ["Left Alt", "Left Shift", "$button_mouse1"], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_select", Side = Side.Left, Keys = ["Left Alt", "$button_mouse2"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_deselect", Side = Side.Left, Keys = ["Left Alt", "$button_mouse1"], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_copy_selection", Side = Side.Left, Keys = ["Left Alt", "$button_mouse0"], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_delete_selection", Side = Side.Left, Keys = [DeleteKey], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_clear_selection", Side = Side.Left, Keys = [ClearKey], Visibility = HintVisibility.SelectionOnly },
        new Hint { Label = "$betterhammer_hint_move", Side = Side.Left, Keys = ["Left Ctrl", "$button_mouse2"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_repair", Side = Side.Right, Keys = ["Left Shift", "$button_mouse1"], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_zoom", Side = Side.Right, Keys = ["Left Alt", ScrollIcon], Visibility = HintVisibility.Always },
        new Hint { Label = "$betterhammer_hint_grid", Side = Side.Right, Keys = ["Left Ctrl", "$button_mouse1"], Visibility = HintVisibility.BuildOnly },
        new Hint { Label = "$betterhammer_hint_grid_distance", Side = Side.Right, Keys = ["Left Ctrl", ScrollIcon], Visibility = HintVisibility.GridOnly },
        new Hint { Label = "$betterhammer_hint_copy_piece", Side = Side.Right, Keys = ["Left Shift", "$KEY_TabLeft", Or, "$KEY_TabRight"], Visibility = HintVisibility.CopyOnly },
    ];

    /// <summary>
    /// Holds the two stacks, so they're shown and hidden together.
    /// </summary>
    private static GameObject _container;
    private static RectTransform _leftStack;
    private static RectTransform _rightStack;
    private static Transform _keyboardRow;
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
            // A hidden hint keeps its space, so the hints around it stay where they are.
            bool shown = hint.Visibility switch
            {
                HintVisibility.BuildOnly => !repairing,
                HintVisibility.GridOnly => !repairing && gridOn,
                HintVisibility.SelectionOnly => MultiCopy.HasSelection,
                HintVisibility.CopyOnly => GhostGroup.Active,
                _ => true,
            };
            hint.Group.alpha = shown ? 1f : 0f;
        }

        if (!_container.activeSelf)
        {
            _container.SetActive(true);
            _layoutDirty = true;
        }

        if (_layoutDirty)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_leftStack);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rightStack);
            _layoutDirty = false;
        }

        AlignStacks();
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
    /// Both stacks sit on top of the vanilla hints. The right stack's right edge lines up with the right
    /// edge of the vanilla "Rotate" icon as drawn (the icon's rect is wider than the sprite inside it), and
    /// the left stack's left edge with the first vanilla hint ("Place").
    /// </summary>
    private static void AlignStacks()
    {
        Vector3[] corners = new Vector3[4];
        _rotateAnchor.GetWorldCorners(corners); // 0 = bottom left, 1 = top left, 2 = top right, 3 = bottom right
        float top = corners[2].y;
        float z = corners[2].z;

        Rect iconRect = _rotateIcon.rect;
        float drawnWidth = Mathf.Min(iconRect.width, iconRect.height * SpriteAspect(_rotateIconImage));
        Vector3 iconCentre = _rotateIcon.TransformPoint(iconRect.center);
        float right = iconCentre.x + (drawnWidth / 2f * _rotateIcon.lossyScale.x);
        _rightStack.position = new Vector3(right, top, z);

        RectTransform first = FirstShownHint(_keyboardRow);
        if (first != null)
        {
            first.GetWorldCorners(corners);
            _leftStack.position = new Vector3(corners[0].x, top, z);
        }
    }

    private static RectTransform FirstShownHint(Transform row)
    {
        for (int i = 0; i < row.childCount; i++)
        {
            if (row.GetChild(i).gameObject.activeSelf)
            {
                return (RectTransform)row.GetChild(i);
            }
        }

        return null;
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

        RectTransform[] stacks = [CreateStack(rect, Side.Left), CreateStack(rect, Side.Right)];

        foreach (Hint hint in Hints)
        {
            // Start from a vanilla multi-key hint (for its row settings), then rebuild its contents.
            GameObject item = Object.Instantiate(copy.gameObject, stacks[(int)hint.Side], false);
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

        // Give matching parts (label, key, "+", key or icon) of a stack's hints the same width, so the
        // keys line up between its rows. All items are active here, so they have a layout to measure.
        foreach (RectTransform stack in stacks)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(stack);
        }

        EqualiseWidths(Side.Left);
        EqualiseWidths(Side.Right);

        _leftStack = stacks[(int)Side.Left];
        _rightStack = stacks[(int)Side.Right];
        _keyboardRow = keyboard;
        _rotateAnchor = (RectTransform)rotate;
        _rotateIcon = (RectTransform)iconTemplate;
        _rotateIconImage = iconTemplate.GetComponent<Image>();
        _container = rect.gameObject;
        _layoutDirty = true;
        return true;
    }

    /// <summary>
    /// One stack of hints, growing upwards from its bottom corner: the left stack from its bottom left,
    /// the right from its bottom right.
    /// </summary>
    private static RectTransform CreateStack(RectTransform parent, Side side)
    {
        RectTransform stack = new GameObject(ContainerName + "_" + side, typeof(RectTransform)).GetComponent<RectTransform>();
        stack.SetParent(parent, false);
        stack.pivot = side == Side.Left ? new Vector2(0f, 0f) : new Vector2(1f, 0f);

        VerticalLayoutGroup layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = RowSpacing;
        layout.childAlignment = side == Side.Left ? TextAnchor.LowerLeft : TextAnchor.LowerRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = stack.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return stack;
    }

    private static void EqualiseWidths(Side side)
    {
        List<Transform> items = [];
        foreach (Hint hint in Hints)
        {
            if (hint.Side == side)
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
        _leftStack = null;
        _rightStack = null;
        _keyboardRow = null;
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

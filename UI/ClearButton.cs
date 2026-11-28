using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace BetterHammer.UI;

/// <summary>
/// A red cross at the right end of a text box that empties it, shown only while something is typed, like the
/// BetterChests search bar. Drawn as an image, since the game's fonts have no "×".
/// </summary>
internal static class ClearButton
{
    private const float Size = 22f;
    private const float Margin = 4f;
    private static readonly Color Colour = new(0.85f, 0.2f, 0.15f);

    private static Sprite _sprite;

    /// <summary>
    /// Adds the cross to the field, and keeps the typed text clear of it.
    /// </summary>
    internal static void Add(TMP_InputField field)
    {
        if (field == null || field.transform.Find("BetterHammer_Clear") != null)
        {
            return;
        }

        GameObject clear = new("BetterHammer_Clear", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)clear.transform;
        rect.SetParent(field.transform, false);
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(Size, Size);
        rect.anchoredPosition = new Vector2(-Margin, 0f);

        Image cross = clear.GetComponent<Image>();
        cross.sprite = Sprite();
        cross.color = Colour;
        cross.preserveAspect = true;

        Button button = clear.GetComponent<Button>();
        button.targetGraphic = cross;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            field.text = "";
            field.DeactivateInputField();
        });

        if (field.textViewport != null)
        {
            field.textViewport.offsetMax = new Vector2(field.textViewport.offsetMax.x - Size - Margin, field.textViewport.offsetMax.y);
        }

        Sync sync = field.gameObject.AddComponent<Sync>();
        sync.Field = field;
        sync.Cross = clear;
        clear.SetActive(field.text.Length > 0);
    }

    /// <summary>
    /// Shows the cross only while something is typed, also when the text is changed without notice (such as being
    /// emptied when the panel opens).
    /// </summary>
    private sealed class Sync : MonoBehaviour
    {
        public TMP_InputField Field;
        public GameObject Cross;

        private void Update()
        {
            bool show = Field != null && Field.text.Length > 0;
            if (Cross != null && Cross.activeSelf != show)
            {
                Cross.SetActive(show);
            }
        }
    }

    /// <summary>A white cross (two thick diagonal strokes, antialiased) on a transparent square, tinted by the image.</summary>
    private static Sprite Sprite()
    {
        if (_sprite != null)
        {
            return _sprite;
        }

        const int size = 32;
        const float halfWidth = 2.6f;
        const float inset = 6f;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                bool inside = px >= inset && px <= size - inset && py >= inset && py <= size - inset;
                float toFirst = Mathf.Abs(px - py) / Mathf.Sqrt(2f);
                float toSecond = Mathf.Abs(px + py - size) / Mathf.Sqrt(2f);
                float alpha = inside ? Mathf.Clamp01(halfWidth + 0.5f - Mathf.Min(toFirst, toSecond)) : 0f;
                pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        _sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        _sprite.hideFlags = HideFlags.HideAndDontSave;
        return _sprite;
    }
}

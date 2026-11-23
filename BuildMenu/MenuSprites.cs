using UnityEngine;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Small icons for the build menu, drawn at runtime so they don't depend on a font having the glyph.
/// </summary>
internal static class MenuSprites
{
    private const int Size = 32;

    /// <summary>
    /// A white triangle pointing down, for open sections. Rotated a quarter turn it points right, for
    /// collapsed ones.
    /// </summary>
    internal static Sprite DownArrow { get; } = CreateDownArrow();

    private static Sprite CreateDownArrow()
    {
        Texture2D texture = new(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        // Corners of the triangle in pixels: top left, top right, bottom middle.
        Vector2 a = new(3f, Size - 7f);
        Vector2 b = new(Size - 3f, Size - 7f);
        Vector2 c = new(Size / 2f, 5f);
        Color32[] pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                float inside = Mathf.Min(Edge(a, b, p), Mathf.Min(Edge(b, c, p), Edge(c, a, p)));
                byte alpha = (byte)(Mathf.Clamp01(inside + 0.5f) * 255f);
                pixels[(y * Size) + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>
    /// Signed distance from p to the line from a to b, positive on the inside of a clockwise triangle.
    /// </summary>
    private static float Edge(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 direction = (b - a).normalized;
        return (direction.x * (a.y - p.y)) - (direction.y * (a.x - p.x));
    }
}

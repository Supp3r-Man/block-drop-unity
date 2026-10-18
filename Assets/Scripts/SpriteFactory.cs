using UnityEngine;

namespace BlockDrop
{
    /// <summary>
    /// The game ships no art. Sprites are generated at start-up as grey-scale shapes and tinted with
    /// SpriteRenderer.color, so there are no image assets to import or commit.
    /// </summary>
    public static class SpriteFactory
    {
        private static Sprite block;
        private static Sprite square;

        /// <summary>
        /// A one-world-unit tile with a light top-left bevel and a darker bottom-right edge, so tinted
        /// tiles read as raised blocks rather than flat squares.
        /// </summary>
        public static Sprite Block()
        {
            if (block != null) return block;
            const int size = 32;
            const int edge = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // y = 0 is the bottom row of the texture.
                    int fromLeft = x, fromRight = size - 1 - x, fromBottom = y, fromTop = size - 1 - y;
                    byte v = 225;
                    if (fromTop < edge || fromLeft < edge) v = 255;          // lit edges
                    if (fromBottom < edge || fromRight < edge) v = 150;      // shaded edges (win in the corners)
                    pixels[y * size + x] = new Color32(v, v, v, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            block = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            block.name = "BlockDrop_Block";
            return block;
        }

        /// <summary>A plain white square one world unit across.</summary>
        public static Sprite Square()
        {
            if (square != null) return square;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            square.name = "BlockDrop_Square";
            return square;
        }
    }
}

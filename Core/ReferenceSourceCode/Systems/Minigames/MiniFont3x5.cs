using UnityEngine;

namespace Game.Systems.Minigames {
    // Digits of the 3x5 minifont (x-height 3, ascender 1, descender 1), drawn
    // as GL quads. Only 0-9 are encoded: minigame HUDs are quad-only and the
    // window title renders text, which is all 2048 needs. Glyphs are 15-bit
    // bitmaps, row-major from the top-left (bit 14 = row 0, col 0).
    public static class MiniFont3x5 {
        public const float GlyphW = 3f;
        public const float GlyphH = 5f;
        // 3 px glyph + 1 px spacing.
        public const float Advance = 4f;

        private static readonly ushort[] Digits = {
            0b111101101101111, // 0
            0b010110010010111, // 1
            0b111001111100111, // 2
            0b111001111001111, // 3
            0b101101111001001, // 4
            0b111100111001111, // 5
            0b111100111101111, // 6
            0b111001001001001, // 7
            0b111101111101111, // 8
            0b111101111001111, // 9
        };

        private static readonly int[] scratch = new int[10];

        // Width of a number with the given digit count at the given scale.
        public static float NumberWidth(int digitCount, float scale) {
            return (digitCount * Advance - 1f) * scale;
        }

        // Emits quads with the current GL.Color; caller must be inside
        // GL.Begin(GL.QUADS).
        public static void DrawNumber(int value, float x, float y, float scale) {
            if (value <= 0) {
                DrawDigit(0, x, y, scale);
                return;
            }
            var count = 0;
            while (value > 0) {
                scratch[count++] = value % 10;
                value /= 10;
            }
            for (var i = 0; i < count; i++) {
                // scratch holds the digits least-significant first.
                DrawDigit(scratch[count - 1 - i], x + i * Advance * scale, y, scale);
            }
        }

        private static void DrawDigit(int digit, float x, float y, float scale) {
            var bits = Digits[digit];
            for (var row = 0; row < 5; row++) {
                // Pixel matrix is y-up, so font row 0 (top) sits highest.
                var py = y + (GlyphH - 1f - row) * scale;
                for (var col = 0; col < 3; col++) {
                    if ((bits & 1 << (14 - (row * 3 + col))) == 0) { continue; }
                    var px = x + col * scale;
                    GL.Vertex3(px, py, 0f);
                    GL.Vertex3(px, py + scale, 0f);
                    GL.Vertex3(px + scale, py + scale, 0f);
                    GL.Vertex3(px + scale, py, 0f);
                }
            }
        }
    }
}

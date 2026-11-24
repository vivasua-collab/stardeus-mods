using UnityEngine;

namespace Game.Systems.Ships {
    // Sizing rules for the hull and flux bars that float under an NPC ship in combat.
    // Pulled out of ShipView.Update so the decisions can be asserted without a camera,
    // a scene or a running combat.
    public static class ShipViewBarScale {
        private const float HPPerBarUnit = 500f;
        private const float MinBarWidth = 2f;
        private const float MaxBarWidth = 25f;

        public static float BarWidth(float maxHP) {
            return Mathf.Clamp(maxHP / HPPerBarUnit, MinBarWidth, MaxBarWidth);
        }

        // onPipLayer is the same condition that decides which camera draws the bars.
        // Sizing them from the PIP camera while the game camera renders them is what
        // made an off-screen ship's bars balloon across the main view.
        public static float OrthoOffset(bool onPipLayer, float pipOrtho, float camOrtho,
                float shipWidth, float barWidth) {
            var ortho = onPipLayer ? pipOrtho : camOrtho;
            // The bar tracks camera zoom so it stays the same size on screen, but it is
            // a label for one ship - it must never dwarf that ship. No lower bound: a floor
            // tied to the ship's width only bites when zoomed in, where it blew the bar up
            // into a block across the screen (STA-1513).
            var maxOffset = shipWidth / barWidth;
            return Mathf.Min(ortho * 0.1f, maxOffset);
        }
    }
}

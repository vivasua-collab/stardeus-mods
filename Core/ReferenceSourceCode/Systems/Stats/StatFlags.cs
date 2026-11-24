using System;

namespace Game.Systems.Stats {
    [Flags]
    public enum StatFlags : uint {
        None = 0,
        Biological = 1 << 1,
        Robotic = 1 << 2,
        /// <summary>
        /// If stat has IsNegative flag, modifiers and effects that are reducing the stat will show up as green, not red
        /// </summary>
        IsNegative = 1 << 3,
        /// <summary>
        /// Only humans and alien species that are biological and human-like, no bots
        /// </summary>
        Humanoid = 1 << 4,
        Animal = 1 << 5,
        Pet = 1 << 6,
        Wild = 1 << 7,
        /// <summary>
        /// For species that can fly by default or things that add ability to fly,
        /// or devices that support only flying beings.
        /// </summary>
        Flying = 1 << 8,
    }
}

using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Modifiers;
using Game.Systems.Story;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    /// <summary>
    /// Implementations must be stateless, multiple regions and sectors can reuse same instance of this.
    /// If implementation has to be saveable / loadable with data, add ISaveable interface.
    /// </summary>
    public abstract class SpaceEffect : IResettable {
        [SelfInit]
        private static void Init() {
            all.Clear();
        }

        protected string AddSectorPrefix(string text) {
            return $"{Texts.Accented(T.StatusPermanent)} {GlyphMap.ArrowRight} {text}";
        }
        private static readonly Dictionary<string, SpaceEffect> all = new();
        /// <summary>
        /// Cleanup any state after game
        /// </summary>
        protected abstract void Cleanup();
        public static void Register(SpaceEffect mod) {
            if (!all.TryAdd(mod.Id, mod)) {
                D.Err("Already registered SpaceEffect: {0}", mod.Id);
                return;
            }
            // The registry resets IResettable entries after every game. Kept only in `all`,
            // Cleanup never ran: a storm that was running, a meteoroid warning and the hit
            // timers carried into the next game (the storm never started there, the shower
            // gave no warning and paused nothing).
            if (Registries.BiomeModifiers.FastGet(Hashes.S(mod.Id)) == null) {
                Registries.BiomeModifiers.Add(mod.Id, mod);
            }
        }
        public static SpaceEffect Get(string id) {
            if (all.TryGetValue(id, out var res)) {
                return res;
            }
            D.Err("Couldn't find SpaceEffect by id: {0}", id);
            return null;
        }
        // A permanent sector effect that is the lasting form of a story event obeys the
        // player's switch for that event (STA-1686). Null for effects with no such event.
        protected virtual string StoryEventId => null;
        private StoryEventDef storyEventDef;
        protected bool IsStoryEventDisabled(GameState s) {
            if (StoryEventId == null) { return false; }
            storyEventDef ??= StoryEventDef.Get(StoryEventId);
            return storyEventDef != null
                && ModifierDisableStoryEvents.IsDisabledFor(s, storyEventDef);
        }
        // The switch can be flipped while the ship sits in the sector. Checked this rarely
        // because the pref lookup builds a string.
        protected static bool IsSwitchCheckTick(long ticks) {
            return ticks % Consts.TicksPer15Minutes == 3;
        }
        public abstract string Id { get; }
        public abstract string Name { get; }
        /// <summary>
        /// Chance to get this biome modifier at region target difficulty
        /// </summary>
        public SpaceEffectConfig Config;
        public abstract void OnEnterSector(GameState s, SpaceSector sector);
        public abstract void OnExitSector(GameState s, SpaceSector sector);
        public abstract void OnLoad(GameState s, SpaceSector sector);
        public abstract void Tick(GameState s, long ticks, SpaceSector currentSector);
        public void OnReset() {
            Cleanup();
        }
        public override string ToString() {
            return $"SpaceEffect[{Id}]";
        }

    }
}

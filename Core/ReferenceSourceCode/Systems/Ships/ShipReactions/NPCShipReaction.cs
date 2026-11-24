using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Ships {
    public abstract class NPCShipReaction {
        protected static void Register(NPCShipReaction re) {
            re.IdH = Hashes.S(re.Id);
            Registries.NPCShipReactions.Add(re.Id, re);
        }
        public abstract string Id { get; }
        public abstract ShipReactionType Type { get; }
        private int? n2pCooldownTicks;
        public virtual int N2PCooldownTicks => n2pCooldownTicks
            ??= (int) (Tunables.NPCShipRepeatInteractionDelayDays * Consts.TicksPerDay);
        /// <summary>
        /// Global (cross-ship) N2P cooldown: once any NPC ship triggers this reaction
        /// toward the player, no NPC ship may select it again until this many ticks
        /// have passed. 0 (default) means no global cooldown. Never applied to N2N
        /// (NPC-to-NPC) interactions.
        /// </summary>
        public virtual int GlobalN2PCooldownTicks => 0;
        public int IdH;

        /// <returns>true if observer will interact with the ship</returns>
        public abstract bool ReactTo(NPCShip observer, NPCShip ship);
        public virtual bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float chanceAdjustment) {
            chanceAdjustment = 0f;
            return true;
        }
        public override string ToString() {
            return $"NPCShipReaction[{Id}]";
        }
    }
}

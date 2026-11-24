// #define DEBUG_NPC_BATTLE

using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Ships {
    public class NPCShipBattle {
        public long StartTick;
        public long EndTick;
        public NPCShip Attacker;
        public NPCShip Defender;
        public float PowerDiff;
        public Rng Rng;
        /// <returns>false when battle is over</returns>
        public bool Tick(GameState s, long ticks) {
            if (ticks >= EndTick) {
#if DEBUG_NPC_BATTLE
                D.Warn($"Battle ended. Attacker: {Attacker.Id}, Defender: {Defender.Id}, AttackerHP: {Attacker.CombatState.HP}, DefenderHP: {Defender.CombatState.HP}");
#endif
                // Battle is over
                return false;
            }
            if (ticks % Consts.TicksPer30Minutes != 21) {
                return true;
            }

            var attackerHitChance = Equations.NPCShipPowerDiffHitChance(PowerDiff);
            var defenderHitChance = Equations.NPCShipPowerDiffHitChance(-PowerDiff);
#if DEBUG_NPC_BATTLE
            D.Warn($"Hit chances. Attacker: {Attacker.Id}, Defender: {Defender.Id}, AttackerHitChance: {attackerHitChance}, DefenderHitChance: {defenderHitChance}");
#endif

            bool canHit = true;
            if (Rng.Chance(attackerHitChance)) {
                var dmg = DamageFor(Attacker);
                Defender.CombatState.DealRandomDamage(dmg, Rng, Attacker);
#if DEBUG_NPC_BATTLE
                D.Warn($"Attacker hit! Attacker: {Attacker.Id}, Defender: {Defender.Id}, Damage: {dmg}, DefenderHP: {Defender.CombatState.HP}");
#endif
                if (Defender.CombatState.HP <= 0) {
                    canHit = false;
#if DEBUG_NPC_BATTLE
                    D.Warn($"Defender destroyed! Attacker: {Attacker.Id}, Defender: {Defender.Id}");
#endif
                }
            }
            if (canHit && Rng.Chance(defenderHitChance)) {
                var dmg = DamageFor(Defender);
                Attacker.CombatState.DealRandomDamage(dmg, Rng, Defender);
#if DEBUG_NPC_BATTLE
                D.Warn($"Defender hit! Attacker: {Attacker.Id}, Defender: {Defender.Id}, Damage: {dmg}, AttackerHP: {Attacker.CombatState.HP}");
#endif
            }

            if (Attacker.CombatState.HP <= 0 || Defender.CombatState.HP <= 0) {
                // Battle is over
                if (Attacker.CombatState.HP <= 0) {
#if DEBUG_NPC_BATTLE
                    D.Warn($"Attacker destroyed! Attacker: {Attacker.Id}, Defender: {Defender.Id}");
#endif
                    Attacker.TurnIntoDerelict(Defender);
                }
                if (Defender.CombatState.HP <= 0) {
#if DEBUG_NPC_BATTLE
                    D.Warn($"Defender destroyed! Attacker: {Attacker.Id}, Defender: {Defender.Id}");
#endif
                    Defender.TurnIntoDerelict(Attacker);
                }
                EndTick = ticks;
                return false;
            }

            return true;
        }

        private float DamageFor(NPCShip ship) {
            var cp = ship.Class.MaxClassPoints * 0.5f;
            var dmg = cp * 0.1f;
            var off = ship.Tag.OffensivenessFor(ship);
            if (off < 0.2f) {
                off = 0.2f;
            }
            dmg += cp * off;
            dmg *= Rng.Range(5.0f, 10.0f);
            dmg += 15f;
            return dmg;
        }
    }
}

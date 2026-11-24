using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Space;
using Game.Systems.Story;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_Attack : NPCShipReaction {
        public const string ReactionId = "Attack";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_Attack());
        }
        public override string Id => ReactionId;
        public override int N2PCooldownTicks => 0;
        public override ShipReactionType Type => ShipReactionType.Negative;

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            // Track organic (non-chase) attacks on the player to enforce cooldown.
            // Retaliation attacks already have ChaseTargetId set and are exempt.
            var isOrganicPlayerAttack = ship.IsPlayer
                && observer.Memory.ChaseTargetId != ship.Id;
            var result = NPCShipBehavior_Sector_Combat.AssignTo(observer, ship);
            if (result && isOrganicPlayerAttack) {
                A.S.Query.GetStoryContext.Ask()
                    ?.AutoCoolDownFlag(StoryEventFlags.NPCShipAttack);
            }
            return result;
        }

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float weightAdjustment) {
            // Derelicts won't fight
            if (ship.IsDerelict || observer.IsDerelict) {
                weightAdjustment = 0f;
                return false;
            }
            if (ship.Faction == observer.Faction) {
                weightAdjustment = 0f;
                return false;
            }
            if (observer.IsQuestGiver && ship.IsPlayer
                    && A.S.Query.IsActiveQuestProvider.Ask(observer)) {
                weightAdjustment = 0f;
                return false;
            }
            // Magnetic storm: peaceful sector - no attacks on the player, retaliation included
            if (ship.IsPlayer && SpaceEffectMagneticStorm.IsOngoing(A.S)) {
                weightAdjustment = 0f;
                return false;
            }

            // we're using 100f to beat all other options, so it's either
            // 0f = no chance at all, or 100f = 99% chance
            // (there are still other options with weights ~ 1f)
            if (observer.Memory.ChaseTargetId == ship.Id) {
                weightAdjustment = 100f;
                return true;
            }

            // Suppress organic unprovoked attacks on the player while cooldown is active.
            // Ships already chasing the player (ChaseTargetId set above) are exempt,
            // preserving retaliation and story/quest-driven combat.
            if (ship.IsPlayer) {
                var ctx = A.S.Query.GetStoryContext.Ask();
                if (ctx != null && ctx.IsCoolingDown(StoryEventFlags.NPCShipAttack)) {
                    weightAdjustment = 0f;
                    return false;
                }
            }

            if (observer.IsPolice) {
                if (ship.IsOutlaw) {
                    // add some police bravery (50% less fear)
                    var fear = observer.FearOf(ship) - 0.5f;
                    D.Warn("Ship may police: {0} -> {1} (fear: {2})",
                        observer, ship, fear);
                    if (!observer.SeededRng(Primes.Prime023).Chance(fear)) {
                        weightAdjustment = 100f;
                        return true;
                    }
                } else {
                    // ship is not outlaw, police won't attack
                    weightAdjustment = 0f;
                    return false;
                }
            }

            if (observer.IsOutlaw) {
                // outlaw bonus (-10% fear)
                var fear = observer.FearOf(ship) - 0.1f;
                weightAdjustment = 1f - fear;
                return true;
            }

            weightAdjustment = 0f;
            return false;
        }
    }
}

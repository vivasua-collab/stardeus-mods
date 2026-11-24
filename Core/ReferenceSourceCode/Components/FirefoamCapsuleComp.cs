using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Components {
    // Firefoam Capsule: pops when a fire within TriggerRadius is one its foam can reach, when
    // the player says so (PopFirefoamCapsuleAction), by chance when it takes damage, and always
    // when it is destroyed. The spray (FirefoamBurst) puts out the fire and leaves foam that
    // keeps fire off until it is cleaned or wears off. Popping removes the tile without
    // dropping anything, and auto rebuild orders a new one.
    // Why it works this way: Docs/Architecture/Systems/Firefoam.md
    public sealed class FirefoamCapsuleComp : BaseComponent<FirefoamCapsuleComp>,
            ITickListener {
        // How often the fuse looks for fire, in ticks (in-game minutes)
        private const int FuseCheckTicks = 6;
        // The ring the pop sends through the air, in tiles and explosion strength
        private const float PopRippleRadius = 3f;
        private const float PopRippleStrength = 50f;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new FirefoamCapsuleComp());
        }

        private float triggerRadius;
        private float radius;
        private Vector2 foamDays;
        private string popSound;
        private bool isPopped;

        public bool CanPop => !isPopped && Tile.IsActive && Tile.IsConstructed;

        protected override void OnConfig() {
            triggerRadius = Config.GetFloat(PropertyIdH.TriggerRadius, 3f);
            radius = Config.GetFloat(PropertyIdH.Radius, 10f);
            foamDays = Config.GetVector2(PropertyIdH.FoamDays, new Vector2(3f, 6f));
            popSound = Config.GetString(PropertyIdH.PopSound, null);
        }

        public override string ToString() {
            return " * Firefoam Capsule";
        }

        public void Tick(long ticks) {
            if (isPopped) { return; }
            // nothing burns anywhere, the usual case
            if (S.Sys.Firex.FlammablesOnFire == 0) { return; }
            if ((ticks + Entity.Id) % FuseCheckTicks != 0) { return; }
            if (!Tile.IsConstructed) { return; }
            if (FirefoamBurst.AnyFireInReach(S, Tile.PosIdx, triggerRadius)) {
                Pop();
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.TookDamage) {
                if (Rng.UChance(PopOnDamageChance())) {
                    Pop();
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                // the breakdown that follows removes the tile and orders the rebuild
                Spray();
            }
        }

        // Rolled the way a damaged explosive device rolls to explode (FlammableComp)
        private float PopOnDamageChance() {
            var health = Tile.Health;
            var chance = CurveDef.FastGet(CurveIdH.Explosiveness_FirefoamCapsule)
                .Evaluate(health.HealthPercent);
            return FlammableComp.AdjustFireOrExplosionChance(chance,
                health.LastDamageAmount / health.Health);
        }

        public void Pop() {
            if (!Spray()) { return; }
            S.CmdQ.Enqueue(new CmdRemoveTile(Tile, false, autoRebuild: true));
        }

        private bool Spray() {
            if (!CanPop) { return false; }
            isPopped = true;
            var pos = Tile.PosIdx;
            FirefoamBurst.Spray(S, pos, radius, foamDays, S.Rng);
            if (S.Clock.Speed < ClockSpeed.Unlimited) {
                var center = EntityUtils.CenterOf(pos);
                if (!string.IsNullOrEmpty(popSound)) {
                    Sounds.PlayLoud(center, popSound, false);
                }
                Explosions.AddRipple(center, PopRippleRadius, PopRippleStrength);
            }
            return true;
        }
    }
}

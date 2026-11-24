using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;

namespace Game.Systems.AI {
    // "Tantrum" breakdown. The being rampages through whatever is closest, beating
    // up one device after another. Devices get damaged, never destroyed: each hit
    // takes a fraction of the health the device has left, so repairs fix everything.
    //
    // A tantrum never causes an explosion - those belong to the sabotage family, a
    // tantrum is supposed to be loud rather than deadly. Most devices can explode,
    // but only once they are damaged enough (see Config/Curves/Explosiveness), so
    // instead of avoiding them a hit is only allowed when the device would still be
    // above that point afterwards. The small chance of a damaged device catching
    // fire is the regular game rule for any damage and is left alone.
    public sealed class AIBreakdownActionTantrum : AIBreakdownActionDeviceSpree {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionTantrum());
        }
        public const string ActId = "BreakdownTantrum";

        // Fraction of the CURRENT health taken by a hit. The low end has to leave a
        // healthy battery above its explosion threshold (0.76), or batteries would
        // never be touched.
        public const float MinDamagePct = 0.2f;
        public const float MaxDamagePct = 0.4f;
        private const int SearchDepth = 30;

        protected override int AiVar => AIVarsH.MentalThrewTantrum;

        private AIBreakdownActionTantrum() : base(ActId, () => T.BreakdownTantrum) {
            // Smashing a device takes hands. Animals have other ways to misbehave.
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Manipulate);
            WithForbiddenFlags(StatFlags.Animal);
        }

        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownTantrumDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }

        // Closest first, so the rampage tears through one room instead of
        // politely walking across the ship between hits
        protected override Tile FindTarget(AIAgentComp agent) {
            return agent.S.Query.FindTileNearby.Ask(
                agent.PosIdx, agent.Motion.IsGrounded, SearchDepth,
                WorldLayer.Objects, (t) => IsValidTarget(agent, t));
        }

        protected override bool IsSpreeTarget(AIAgentComp agent, Tile t) {
            if (t.Health == null || t.Health.IsDestroyed) { return false; }
            // Damage can only be dealt to the root tile of a multi-tile device
            if (t.Transform == null || t.Transform.IsChild) { return false; }
            if (t.TryGetComponent<MatterReactorComp>(out _)) { return false; }
            // Already too damaged to take even the weakest hit without a risk of
            // blowing up. This also rules out things that explode at any health.
            return IsSafeHit(t, MinDamagePct);
        }

        // True when the device has no chance to explode at the health this hit would
        // leave it with. Mirrors FlammableComp.ReactToDamage, which evaluates the
        // explosiveness curve after the damage has been applied.
        public static bool IsSafeHit(Tile t, float damagePct) {
            var flammable = t.Flammable;
            if (flammable == null || !flammable.CanExplode) { return true; }
            var curve = flammable.ExplosivenessCurve;
            if (curve == null) { return true; }
            var healthPctAfter = t.Health.HealthPercent * (1f - damagePct);
            return curve.Evaluate(healthPctAfter) <= 0f;
        }

        protected override void WhileActing(AIAgentComp agent, Tile target, long ticks) {
            agent.Being.Graphics.Punch(target.Position - agent.Position,
                0.15f, 0.1f, 0.2f);
        }

        protected override void ActOn(AIAgentComp agent, Tile target, long ticks) {
            // A fraction of the CURRENT health can never bring the device to zero.
            // Hit as hard as the roll says, unless that would make the device unstable -
            // the weakest hit is known to be safe, IsSpreeTarget was just checked.
            var damagePct = Rng.URange(MinDamagePct, MaxDamagePct);
            if (!IsSafeHit(target, damagePct)) {
                damagePct = MinDamagePct;
            }
            target.Health.TakeDamage(target.Health.Health * damagePct, DamageType.Blunt);
            Announce(agent, T.BreakdownTantrumHit_V2.F(
                Texts.LinkToBeing(agent.BeingData), Texts.LinkToTile(target)));
        }
    }
}

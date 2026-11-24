using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Systems.Mental {
    /// <summary>
    /// Gives colonists trust effects for what the AI does (Mind.Trust holds them) and keeps
    /// them applied: expiry, standing effects, and putting modifiers back after a load.
    /// </summary>
    public sealed class TrustSys : GameSystem {
        public const string SysId = "TrustSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new TrustSys());
        }

        public const string ReasonPersonalQuarters = "trust.personal.quarters";
        public const string ReasonNeglectDeath = "trust.crew.neglect.death";
        public const string ReasonCrewSold = "trust.crew.sold";

        private bool needsSweep;

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Sig.BeingDied.AddListener(OnBeingDied);
        }

        public override void Unload() {
            needsSweep = false;
        }

        private void OnLoadState(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            // AfterLoadState fires before beings are fully restored, so the modifiers go
            // on with the first tick instead
            needsSweep = true;
        }

        private void OnTick(long ticks) {
            if (!needsSweep && ticks % Consts.TicksPerHour != 23) { return; }
            needsSweep = false;
            Sweep(ticks);
        }

        private void Sweep(long ticks) {
            var areas = S.Sys.Areas;
            foreach (var data in S.Query.AllBeingData.Ask().Values) {
                var mind = data?.Mind;
                if (mind == null) { continue; }
                var trust = mind.Trust;
                trust.Expire(ticks);
                // Only while aboard: a colonist away keeps what they had when they left
                if (data.Being != null && IsColonist(data)) {
                    trust.SetHeld(TrustSourceIdH.PersonalQuarters, Tunables.TrustPersonalQuarters,
                        ticks, ReasonPersonalQuarters, areas.HasPersonalQuarters(data));
                }
                trust.Sync();
            }
        }

        /// <summary>
        /// A living crew member whose trust counts: a Player human with a mood, not a guest
        /// and not a sleeper on the manifest.
        /// </summary>
        public static bool IsColonist(BeingData data) {
            return data.HasStateFlag(BeingState.Alive) && IsCrewMind(data);
        }

        private static bool IsCrewMind(BeingData data) {
            var mind = data.Mind;
            if (mind?.Health == null || mind.Faction?.IsPlayer != true) { return false; }
            if (data.Species == null || !data.Species.IsHumanoid || !data.Species.IsBiological) {
                return false;
            }
            return !data.IsGuest && !data.IsInStasis;
        }

        private void OnBeingDied(BeingData dead) {
            if (dead?.Mind == null || !IsCrewMind(dead) || !DiedOfNeglect(dead)) { return; }
            GiveCrew(S, dead, TrustSourceIdH.NeglectDeath, Tunables.TrustNeglectDeath,
                Tunables.TrustNeglectDeathDays, ReasonNeglectDeath);
        }

        // Deaths the AI should have prevented and was warned about: no food, no air, no
        // heating or cooling. The killing condition is still on the body when BeingDied fires.
        private static bool DiedOfNeglect(BeingData dead) {
            return dead.HasCondition(ConditionIdH.Starvation)
                || dead.HasCondition(ConditionIdH.Hypoxia)
                || dead.HasCondition(ConditionIdH.Hypothermia)
                || dead.HasCondition(ConditionIdH.Hyperthermia);
        }

        public static void OnCrewSold(GameState s, BeingData sold) {
            GiveCrew(s, sold, TrustSourceIdH.CrewSold, Tunables.TrustCrewSold,
                Tunables.TrustCrewSoldDays, ReasonCrewSold);
        }

        private static void GiveCrew(GameState s, BeingData about, int sourceIdH, int points,
                int days, string reasonKey) {
            var ticks = G.Ticks;
            var duration = (long) days * Consts.TicksPerDay;
            foreach (var data in s.Query.AllBeingData.Ask().Values) {
                if (data == about || !IsColonist(data)) { continue; }
                data.Mind.Trust.Add(sourceIdH, points, ticks, duration, reasonKey);
            }
        }
    }
}

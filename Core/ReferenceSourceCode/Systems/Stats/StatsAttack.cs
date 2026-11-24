using Game.CodeGen;
using Game.Constants;
using Game.Data;
using KL.Utils;
using static Game.Systems.Stats.Stat;

namespace Game.Systems.Stats {
    public sealed class StatsAttack : StatsContainer {
        public readonly Stat Quality = new Stat(StatId.Quality, StatCategoryId.Weapon, ShowAs.Percentage, icon: IconId.CBookmark, nameT: T.NounQuality);
        public readonly Stat DPS = new Stat(StatId.DPS, StatCategoryId.Weapon, ShowAs.Float, icon: IconId.CAttackTarget);
        public readonly Stat ChargeSec = new Stat(StatId.ChargeSec, StatCategoryId.Weapon, ShowAs.Float, icon: IconId.CTime);
        public readonly Stat CooldownSec = new Stat(StatId.CooldownSec, StatCategoryId.Weapon, ShowAs.Float, icon: IconId.CTime);
        // Move speed multiplier during the attack, including charge and cooldown
        public readonly Stat MoveSpeedMul = new Stat(StatId.MoveSpeedMul, StatCategoryId.Weapon, ShowAs.Percentage, icon: IconId.CHaul);
        public readonly Stat StunChanceBio = new Stat(StatId.StunChanceBio, StatCategoryId.Weapon, ShowAs.Percentage, icon: IconId.CCross);
        public readonly Stat StunChanceRobo = new Stat(StatId.StunChanceRobo, StatCategoryId.Weapon, ShowAs.Percentage, icon: IconId.CCross);
        public readonly Stat AccuracyMax = new Stat(StatId.AccuracyMax, StatCategoryId.Weapon, ShowAs.Percentage, icon: IconId.CPercentGreen);
        public readonly Stat RangeMin = new Stat(StatId.RangeMin, StatCategoryId.Weapon, ShowAs.Float, icon: IconId.CAttackTarget);
        public readonly Stat RangeMax = new Stat(StatId.RangeMax, StatCategoryId.Weapon, ShowAs.Float, icon: IconId.CAttackTarget);
        public readonly Stat BleedChance = new Stat(StatId.BleedChance, StatCategoryId.Weapon, ShowAs.Percentage, icon: IconId.CHealth);
        // The share of the target's worn armor a hit ignores (BodyPartSlot.GetHitSeverityIncrease)
        public readonly Stat ArmorPenetration = new Stat(StatId.ArmorPenetration, StatCategoryId.Weapon, ShowAs.Percentage, icon: IconId.CShield);
        public Stat[] ShortStats;
        public Stat[] FullStats;
        public StatsAttack() {
            AllStats = new[] {
                Quality,
                DPS,
                ChargeSec,
                CooldownSec,
                MoveSpeedMul,
                StunChanceBio,
                StunChanceRobo,
                AccuracyMax,
                RangeMin,
                RangeMax,
                BleedChance,
                ArmorPenetration,
            };
            FullStats = new[] {
                DPS,
                ChargeSec,
                CooldownSec,
                MoveSpeedMul,
                StunChanceBio,
                StunChanceRobo,
                AccuracyMax,
                RangeMin,
                RangeMax,
                ArmorPenetration,
                // BleedChance is not shown: whether a wound bleeds comes from its severity and
                // damage type (Wound.GetBleedChance), so the weapon's value promised nothing
            };
            ShortStats = new[] {
                DPS,
                ChargeSec,
                CooldownSec,
                AccuracyMax,
                RangeMax,
            };
        }
        private static Curve qualityDPS;
        private static Curve qualityMaxAcc;

        protected override void LinkDependencies() {
            if (qualityDPS == null) {
                qualityDPS = CurveDef.FastGet(CurveIdH.Attacks_QualityDPS);
                qualityMaxAcc = CurveDef.FastGet(CurveIdH.Attacks_QualityMaxAccuracy);
            }

            StatDependency.CreateForCurve(DPS, Quality, qualityDPS, ignoreZero: true);
            StatDependency.CreateForCurve(AccuracyMax, Quality, qualityMaxAcc, ignoreZero: true);
        }
    }
}

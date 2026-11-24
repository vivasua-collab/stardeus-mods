using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;
using static Game.Systems.Stats.Stat;

namespace Game.Systems.Stats {
    public sealed class StatsBeing : StatsContainer {
        public readonly Stat[] PersistentMentalStats;
        // Environment
        public readonly Stat AIAlignment = new Stat(StatId.AIAlignment,
            StatCategoryId.Mental, ShowAs.Percentage, icon: IconId.CRobotEyeGreen);
        public readonly Stat SpaceSuitIntegrity = new Stat(StatId.SpaceSuitIntegrity,
            StatCategoryId.Environment, ShowAs.Percentage, icon: IconId.COxygen)
            .WithRequiredFlags(StatFlags.Humanoid);
        public readonly Stat O2 = new Stat(StatId.O2, StatCategoryId.Environment,
            ShowAs.Percentage, T.OxygenLevel, icon: IconId.COxygen)
                .WithRequiredNeed(NeedIdH.Oxygen);
        public readonly Stat O2Min = new Stat(StatId.O2Min, StatCategoryId.Environment,
            ShowAs.Percentage, icon: IconId.CLowO2)
                .WithRequiredNeed(NeedIdH.Oxygen);
        public readonly Stat Temp = new Stat(StatId.Temp, StatCategoryId.Environment,
            ShowAs.Temperature, T.Temperature, icon: IconId.CTemperature);
        public readonly Stat TempMin = new Stat(StatId.TempMin, StatCategoryId.Environment,
            ShowAs.Temperature, icon: IconId.CTemperature)
                .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat TempMax = new Stat(StatId.TempMax, StatCategoryId.Environment,
            ShowAs.Temperature, icon: IconId.CTemperature);
        // Mind Functions
        public readonly Stat Fun = new Stat(StatId.Fun, StatCategoryId.Mental,
            ShowAs.Percentage, icon: IconId.CMoodGood)
                .WithRequiredNeed(NeedIdH.Fun);
        public readonly Stat FunDropRate = new Stat(StatId.FunDropRate,
            StatCategoryId.Mental, ShowAs.PercentPerHour, icon: IconId.CMoodBad)
                .WithFlags(StatFlags.IsNegative)
                .WithRequiredNeed(NeedIdH.Fun);
        public readonly Stat FunIncreaseRate = new Stat(StatId.FunIncreaseRate,
            StatCategoryId.Mental, ShowAs.PercentPerHour, icon: IconId.CMoodGood)
                .WithRequiredNeed(NeedIdH.Fun);
        public readonly Stat DepressionTendency = new Stat(StatId.DepressionTendency,
            StatCategoryId.Mental, ShowAs.PercentageSigned, icon: IconId.CMoodNeutral)
                .WithFlags(StatFlags.IsNegative)
                .WithRequiredNeed(NeedIdH.Fun);
        // Body Functions
        public readonly Stat MoveSpeed = new Stat(StatId.MoveSpeed,
            StatCategoryId.Ability, ShowAs.RatePerMinute, icon: IconId.CHaul);
        public readonly Stat BatteryCharge = new Stat(StatId.BatteryCharge,
            StatCategoryId.Need, ShowAs.Int, icon: IconId.CBatteryFull)
                .WithRequiredNeed(NeedIdH.Electricity);
        public readonly Stat BatteryCapacity = new Stat(StatId.BatteryCapacity,
            StatCategoryId.Need, ShowAs.Int, icon: IconId.CBatteryFull)
                .WithRequiredNeed(NeedIdH.Electricity);
        public readonly Stat BatteryConsumption = new Stat(StatId.BatteryConsumption,
            StatCategoryId.Need, ShowAs.RatePerDay, icon: IconId.CEnergy)
                .WithRequiredNeed(NeedIdH.Electricity);
        public readonly Stat Dexterity = new Stat(StatId.Dexterity,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.COperate);
        public readonly Stat Sight = new Stat(StatId.Sight,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.WEye)
            .WithRequiredBodyPart(BodyPartIdH.Eye);
        public readonly Stat Light = new Stat(StatId.Light,
            StatCategoryId.Environment, ShowAs.Percentage, icon: IconId.CLightbulb)
            .WithRequiredNeed(NeedIdH.Light);
        public readonly Stat CarryCapacity = new Stat(StatId.CarryCapacity,
            StatCategoryId.Ability, ShowAs.Int, icon: IconId.CHaul)
                .WithRequiredAbility(AbilityIdH.Carry);

        // Derived abilities: no sources of their own, only dependencies on the stats above,
        // so their tooltips explain what slows a worker or spoils a shot (body capacity
        // factors). See Docs/Architecture/Systems/StatsAndDependencies.md
        public readonly Stat WorkSpeed = new Stat(StatId.WorkSpeed,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.CCrafting)
                .WithDefaultBase(1f);
        public readonly Stat ManualWorkSpeed = new Stat(StatId.ManualWorkSpeed,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.CHammer)
                .WithDefaultBase(1f);
        public readonly Stat ShootingAccuracy = new Stat(StatId.ShootingAccuracy,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.CTarget)
                .WithDefaultBase(1f)
                .WithRequiredAbility(AbilityIdH.UseItem);
        // The melee hit chance itself: melee weapons have no accuracy. 50% for a
        // being with no Melee skill, the skill adds up to +42% (Attacks/MeleeSkill_Accuracy),
        // Sight and Dexterity scale it
        public const float MeleeHitChanceBase = 0.5f;
        public readonly Stat MeleeAccuracy = new Stat(StatId.MeleeAccuracy,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.CFist)
                .WithDefaultBase(MeleeHitChanceBase);
        // Chance to avoid a melee hit that would have landed: from the Melee skill
        // (SkillStatSource) plus an added amount from Sight
        public readonly Stat MeleeDodge = new Stat(StatId.MeleeDodge,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.CCombat);
        public readonly Stat SurgerySuccess = new Stat(StatId.SurgerySuccess,
            StatCategoryId.Ability, ShowAs.Percentage, icon: IconId.CHealth)
                .WithDefaultBase(1f);

        // Defense stats
        public readonly Stat ArmorSharp = new Stat(StatId.ArmorSharp,
            StatCategoryId.Armor, ShowAs.Float, icon: IconId.CShield);
        public readonly Stat ArmorPiercing = new Stat(StatId.ArmorPiercing,
            StatCategoryId.Armor, ShowAs.Float, icon: IconId.CShield);
        public readonly Stat ArmorBlunt = new Stat(StatId.ArmorBlunt,
            StatCategoryId.Armor,
            ShowAs.Float, icon: IconId.CShield);
        public readonly Stat ArmorEnergy = new Stat(StatId.ArmorEnergy,
            StatCategoryId.Armor, ShowAs.Float, icon: IconId.CShield);
        // How much of the chance for a hit to find a gap in the armor or land a crit the
        // armor takes away (BodyPartSlot.GetHitSeverityIncrease), from suits and helmets
        public readonly Stat WeakPointCover = new Stat(StatId.WeakPointCover,
            StatCategoryId.Armor, ShowAs.Percentage, icon: IconId.CShield);

        // Health stats
        public readonly Stat BloodAmount = new Stat(StatId.BloodAmount,
            StatCategoryId.Health, ShowAs.Milliliters, icon: IconId.CBlood)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat BloodCapacity = new Stat(StatId.BloodCapacity,
            StatCategoryId.Health, ShowAs.Milliliters, icon: IconId.CBlood)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat WoundHealRate = new Stat(StatId.WoundHealRate,
            StatCategoryId.Health, ShowAs.RatePerDay, icon: IconId.CHealth)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat MaintenanceRate = new Stat(StatId.MaintenanceRate,
            StatCategoryId.Health, ShowAs.RatePerDay, icon: IconId.CWrench2)
            .WithRequiredFlags(StatFlags.Robotic);
        public readonly Stat BloodClotRate = new Stat(StatId.BloodClotRate,
            StatCategoryId.Health, ShowAs.RatePerHour, icon: IconId.CBlood)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat BloodLossRate = new Stat(StatId.BloodLossRate,
            StatCategoryId.Health, ShowAs.PercentPerHourRaw, icon: IconId.CBlood)
            .WithRequiredFlags(StatFlags.Biological)
            .WithFlags(StatFlags.IsNegative);
        public readonly Stat BloodRegenRate = new Stat(StatId.BloodRegenRate,
            StatCategoryId.Health, ShowAs.PercentPerHourRaw, icon: IconId.CBlood)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat ImmunityGainRate = new Stat(StatId.ImmunityGainRate,
            StatCategoryId.Health, ShowAs.PercentPerDayRaw, icon: IconId.CHealth)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat ImmunityLossRate = new Stat(StatId.ImmunityLossRate,
            StatCategoryId.Health, ShowAs.PercentPerDayRaw, icon: IconId.CHealth)
            .WithRequiredFlags(StatFlags.Biological)
            .WithFlags(StatFlags.IsNegative);
        public readonly Stat SurgicalFatigue = new Stat(StatId.SurgicalFatigue,
            StatCategoryId.Health, ShowAs.Percentage, icon: IconId.CHealth)
            .WithRequiredFlags(StatFlags.Biological);
        // Pain: the sum of what every wound hurts (WoundPainSource). Above
        // PainThreshold a being goes down in pain shock, a robot in system shock. A robot has
        // it too, hidden: it drives the shutdown, but pain is not a word for a machine.
        public readonly Stat Pain = new Stat(StatId.Pain,
            StatCategoryId.Health, ShowAs.Percentage, icon: IconId.CStress)
            .WithFlags(StatFlags.IsNegative)
            .WithHiddenFor(StatFlags.Robotic);
        public readonly Stat PainThreshold = new Stat(StatId.PainThreshold,
            StatCategoryId.Health, ShowAs.Percentage, icon: IconId.CStress)
            .WithDefaultBase(DefaultPainThreshold)
            .WithHiddenFor(StatFlags.Robotic);
        // pain past this downs a being in pain shock
        public const float DefaultPainThreshold = 0.8f;
        public readonly WoundPainSource WoundPain = new();

        public readonly Stat Sleep = new Stat(StatId.Sleep, StatCategoryId.Need,
            ShowAs.Percentage, icon: IconId.CSleep)
            .WithRequiredNeed(NeedIdH.Sleep);
        public readonly Stat SleepRate = new Stat(StatId.SleepRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CSleep)
            .WithRequiredNeed(NeedIdH.Sleep);
        public readonly Stat SleepPressureRate = new Stat(StatId.SleepPressureRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CSleep)
            .WithFlags(StatFlags.IsNegative)
            .WithRequiredNeed(NeedIdH.Sleep);

        // Needs
        public readonly Stat LungEfficiency = new Stat(StatId.LungEfficiency,
            StatCategoryId.Health, ShowAs.Percentage, icon: IconId.COxygen)
            .WithRequiredNeed(NeedIdH.Oxygen)
            .WithRequiredBodyPart(BodyPartIdH.Lung);
        public readonly Stat LungCapacity = new Stat(StatId.LungCapacity,
        StatCategoryId.Health,
            ShowAs.Milliliters, icon: IconId.COxygen)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredBodyPart(BodyPartIdH.Lung);
        public readonly Stat BloodToxicity = new Stat(StatId.BloodToxicity,
            StatCategoryId.Health, ShowAs.Percentage, icon: IconId.CPoison)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat BloodFilterRate = new Stat(StatId.BloodFilterRate,
            StatCategoryId.Health, ShowAs.RatePerHour, icon: IconId.CPoison)
            .WithRequiredFlags(StatFlags.Biological);
        public readonly Stat Hunger = new Stat(StatId.Hunger,
            StatCategoryId.Need, ShowAs.Percentage, icon: IconId.CFood)
            .WithRequiredNeed(NeedIdH.Hunger);
        public readonly Stat HungerRate = new Stat(StatId.HungerRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CFood)
            .WithRequiredNeed(NeedIdH.Hunger);
        public readonly Stat HungerSatisfyRate = new Stat(StatId.HungerSatisfyRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CFood)
            .WithRequiredNeed(NeedIdH.Hunger);
        public readonly Stat DigestionRate = new Stat(StatId.DigestionRate,
            StatCategoryId.Health, ShowAs.RatePerHour, icon: IconId.CFood)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredBodyPart(BodyPartIdH.Stomach);
        public readonly Stat Sanity = new Stat(StatId.Sanity,
            StatCategoryId.Mental, ShowAs.Percentage, icon: IconId.CTraits)
            .WithRequiredFlags(StatFlags.Biological)
            .WithForbiddenFlags(StatFlags.Wild);
        public readonly Stat SanityBaseline = new Stat(StatId.SanityBaseline,
            StatCategoryId.Mental, ShowAs.Percentage, icon: IconId.CTraits)
            .WithRequiredFlags(StatFlags.Biological)
            .WithForbiddenFlags(StatFlags.Wild);
        public readonly Stat SanityTarget = new Stat(StatId.SanityTarget,
            StatCategoryId.Mental, ShowAs.Percentage, icon: IconId.CTraits)
            .WithRequiredFlags(StatFlags.Biological)
            .WithForbiddenFlags(StatFlags.Wild);
        // Sanity at or below which a being becomes eligible for light/medium/heavy
        // mental breakdown checks. Traits (e.g. Enlightened) tune these via modifiers.
        public readonly Stat MentalBreakdownLightThreshold = new Stat(StatId.MentalBreakdownLightThreshold,
            StatCategoryId.Mental, ShowAs.Percentage, icon: IconId.CStress)
            .WithRequiredFlags(StatFlags.Biological)
            .WithForbiddenFlags(StatFlags.Wild);
        public readonly Stat MentalBreakdownMediumThreshold = new Stat(StatId.MentalBreakdownMediumThreshold,
            StatCategoryId.Mental, ShowAs.Percentage, icon: IconId.CStress)
            .WithRequiredFlags(StatFlags.Biological)
            .WithForbiddenFlags(StatFlags.Wild);
        public readonly Stat MentalBreakdownHeavyThreshold = new Stat(StatId.MentalBreakdownHeavyThreshold,
            StatCategoryId.Mental, ShowAs.Percentage, icon: IconId.CStress)
            .WithRequiredFlags(StatFlags.Biological)
            .WithForbiddenFlags(StatFlags.Wild);
        public readonly Stat StomachCapacity = new Stat(StatId.StomachCapacity,
            StatCategoryId.Health, ShowAs.Float, icon: IconId.CFood)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredBodyPart(BodyPartIdH.Stomach);
        public readonly Stat StomachContents = new Stat(StatId.StomachContents,
            StatCategoryId.Health, ShowAs.Float, icon: IconId.CFood)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredBodyPart(BodyPartIdH.Stomach);
        public readonly Stat IntestinesCapacity = new Stat(StatId.IntestinesCapacity,
            StatCategoryId.Health, ShowAs.Float, icon: IconId.CFood)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredBodyPart(BodyPartIdH.Intestines);
        public readonly Stat IntestinesContents = new Stat(StatId.IntestinesContents,
            StatCategoryId.Health, ShowAs.Float, icon: IconId.CFood)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredBodyPart(BodyPartIdH.Intestines);
        public readonly Stat Toilet = new Stat(StatId.Toilet,
            StatCategoryId.Need, ShowAs.Percentage, icon: IconId.CToilet)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredNeed(NeedIdH.Toilet);
        /// <summary>
        /// How soon will a being have to use a toilet again
        /// </summary>
        public readonly Stat ToiletRate = new Stat(StatId.ToiletRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CToilet)
            .WithRequiredFlags(StatFlags.Biological)
            .WithFlags(StatFlags.IsNegative)
            .WithRequiredNeed(NeedIdH.Toilet);
        /// <summary>
        /// How fast we poop
        /// </summary>
        public readonly Stat ToiletReliefRate = new Stat(StatId.ToiletReliefRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CToilet)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredNeed(NeedIdH.Toilet);
        public readonly Stat Hygiene = new Stat(StatId.Hygiene,
            StatCategoryId.Need, ShowAs.Percentage, icon: IconId.CShower)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredNeed(NeedIdH.Hygiene);
        public readonly Stat HygieneReliefRate = new Stat(StatId.HygieneReliefRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CToilet)
            .WithRequiredFlags(StatFlags.Biological)
            .WithRequiredNeed(NeedIdH.Hygiene);
        /// <summary>
        /// How soon will a being have to cleanup again
        /// </summary>
        public readonly Stat HygieneRate = new Stat(StatId.HygieneRate,
            StatCategoryId.Need, ShowAs.PercentPerHour, icon: IconId.CShower)
            .WithRequiredFlags(StatFlags.Biological)
            .WithFlags(StatFlags.IsNegative)
            .WithRequiredNeed(NeedIdH.Hygiene);
        public StatsBeing() {
            // StatsSys applies these last to first each tick, so a stat comes before the stats
            // it depends on: then Light -> Sight -> WorkSpeed -> ManualWorkSpeed settle within
            // one tick. Saves match stats by id, so the order is free to change.
            AllStats = new[] {
                ManualWorkSpeed,
                WorkSpeed,
                ShootingAccuracy,
                MeleeAccuracy,
                MeleeDodge,
                SurgerySuccess,
                Pain,
                PainThreshold,
                AIAlignment,
                ArmorBlunt,
                ArmorEnergy,
                ArmorPiercing,
                ArmorSharp,
                WeakPointCover,
                BatteryCapacity,
                BatteryCharge,
                BatteryConsumption,
                BloodAmount,
                BloodCapacity,
                BloodClotRate,
                BloodFilterRate,
                BloodLossRate,
                BloodRegenRate,
                BloodToxicity,
                CarryCapacity,
                DepressionTendency,
                Dexterity,
                DigestionRate,
                Fun,
                FunDropRate,
                FunIncreaseRate,
                Hunger,
                HungerRate,
                HungerSatisfyRate,
                Hygiene,
                HygieneRate,
                HygieneReliefRate,
                IntestinesCapacity,
                IntestinesContents,
                ImmunityGainRate,
                ImmunityLossRate,
                LungCapacity,
                LungEfficiency,
                MaintenanceRate,
                MentalBreakdownLightThreshold,
                MentalBreakdownMediumThreshold,
                MentalBreakdownHeavyThreshold,
                MoveSpeed,
                O2,
                O2Min,
                Sanity,
                SanityBaseline,
                SanityTarget,
                Sight,
                Sleep,
                SleepPressureRate,
                SleepRate,
                SurgicalFatigue,
                SpaceSuitIntegrity,
                StomachCapacity,
                StomachContents,
                Temp,
                TempMax,
                TempMin,
                Toilet,
                ToiletRate,
                ToiletReliefRate,
                WoundHealRate,
                Light,
            };

            PersistentMentalStats = new[] {
                AIAlignment,
                Fun,
                DepressionTendency,
                Sanity,
                SanityBaseline,
                SanityTarget,
                MentalBreakdownLightThreshold,
                MentalBreakdownMediumThreshold,
                MentalBreakdownHeavyThreshold,
            };
            if (PersistentMentalStats.Length != Mind.GenMentalStatsLength) {
                D.Err("Check StatsBeing.PersistentMentalStats! Id count does not match!");
            }
        }
        public void ActivateFor(BeingData d) {
            foreach (var stat in AllStats) {
                bool hasForbiddenFlag = (stat.FlagsForbidden & d.Species.StatFlags) != StatFlags.None;
                if (hasForbiddenFlag) {
                    stat.MarkUnused();
                }
                if (stat.FlagsRequired != StatFlags.None) {
                    bool hasRequiredFlag = (stat.FlagsRequired & d.Species.StatFlags) != StatFlags.None;
                    if (!hasRequiredFlag) {
                        stat.MarkUnused();
                    }
                }
                #if !PRODUCTION_BUILD
                stat.Owner = d;
                #endif
            }
            SkillStatSource.AddAllTo(d);
            AddStatSource(Pain.Id, WoundPain);
            Pain.SourcesChanged();
            if (d.Species.IsBiological) {
                LinkPainEffects();
            }
        }

        // Pain dims the hands (Dexterity). Its mood is the Injured condition's pain effect, the
        // one the Sanity tab lists; a stat link to SanityTarget counted pain a second time.
        // Flesh only: a robot's hidden pain is the strain that drives its system shock, and a
        // machine feels none. Linked here, not in LinkDependencies, which runs before
        // the species is known.
        private bool painEffectsLinked;
        private void LinkPainEffects() {
            if (painEffectsLinked) { return; }
            painEffectsLinked = true;
            StatDependency.CreateForCurve(Dexterity, Pain,
                CurveFor(CurveIdH.Beings_Pain_Dexterity), ignoreZero: true);
        }

        protected override void LinkDependencies() {
            StatDependency.CreateForCurve(Sight, Light,
                CurveFor(CurveIdH.Beings_Light_Sight), ignoreZero: false);
            StatDependency.CreateForCurve(MoveSpeed, Sight,
                CurveFor(CurveIdH.Beings_Sight_MoveSpeed), ignoreZero: false);
            StatDependency.CreateForCurve(MoveSpeed, LungEfficiency,
                CurveFor(CurveIdH.Beings_LungEfficiency_MoveSpeed), ignoreZero: false);
            StatDependency.CreateForCurve(MoveSpeed, Light,
                CurveFor(CurveIdH.Beings_Light_Darkness), ignoreZero: false);
            StatDependency.CreateForCurve(BloodRegenRate, Hunger,
                CurveFor(CurveIdH.Beings_Hunger_BloodRegenRate));
            StatDependency.CreateForCurve(BloodRegenRate, BloodLossRate,
                CurveFor(CurveIdH.Beings_BloodLossRate_BloodRegenRate), ignoreZero: true);
            // Hands already carry their own amounts, so Dexterity only nudges the total
            StatDependency.CreateForCurve(CarryCapacity, Dexterity,
                CurveFor(CurveIdH.Beings_Dexterity_CarryCapacity), ignoreZero: true);

            // Dexterity ignores 0: animals (paws give none) and handless bots are not clumsy,
            // they just have no hands to be judged by
            StatDependency.CreateForCurve(WorkSpeed, Light,
                CurveFor(CurveIdH.Beings_Light_Darkness), ignoreZero: false);
            StatDependency.CreateForCurve(WorkSpeed, Sight,
                CurveFor(CurveIdH.Beings_Sight_WorkSpeed), ignoreZero: false);
            StatDependency.CreateFor(ManualWorkSpeed, WorkSpeed, 1f);
            StatDependency.CreateFor(ManualWorkSpeed, Dexterity, 1f, ignoreZero: true);

            StatDependency.CreateForCurve(ShootingAccuracy, Sight,
                CurveFor(CurveIdH.Attacks_Sight_ShootingAccuracy), ignoreZero: false);
            StatDependency.CreateForCurve(ShootingAccuracy, Dexterity,
                CurveFor(CurveIdH.Attacks_Dexterity_ShootingAccuracy), ignoreZero: true);
            var meleeCurve = CurveFor(CurveIdH.Attacks_Capacity_MeleeAccuracy);
            StatDependency.CreateForCurve(MeleeAccuracy, Sight, meleeCurve, ignoreZero: false);
            StatDependency.CreateForCurve(MeleeAccuracy, Dexterity, meleeCurve, ignoreZero: true);
            StatDependency.CreateForCurveAdditive(MeleeDodge, Sight,
                CurveFor(CurveIdH.Attacks_Sight_MeleeDodge));

            StatDependency.CreateFor(SurgerySuccess, Dexterity, 1f, ignoreZero: true);
            StatDependency.CreateForCurve(SurgerySuccess, Sight,
                CurveFor(CurveIdH.Surgery_Sight_SurgerySuccess), ignoreZero: false);
            StatDependency.CreateForCurve(SurgerySuccess, Light,
                CurveFor(CurveIdH.Surgery_Light_SurgerySuccess), ignoreZero: false);
        }

        // A missing curve must not leave a null: StatDependency would fall back to its
        // multiplier, which is 0 for a curve dependency, and zero the stat
        private static Curve CurveFor(int idH) {
            var curve = CurveDef.FastGet(idH);
            if (curve == null) {
                D.Err("Stat dependency curve missing: {0}", CurveIdH.ResolveName(idH));
                return 1f;
            }
            return curve;
        }

        public void ApplyNeeds(List<NeedSource> needSources) {
            foreach (var stat in AllStats) {
                if (stat.RequiredNeedId != 0) {
                    bool hasNeed = false;
                    foreach (var ns in needSources) {
                        if (ns.Need.IdH == stat.RequiredNeedId) {
                            hasNeed = true;
                            break;
                        }
                    }
                    // we have to flip
                    bool isUnused = !hasNeed;
                    if (stat.IsUnused != isUnused) {
                        stat.IsUnused = isUnused;
                        if (stat.Influences != null) {
                            // reapply dependencies
                            foreach (var inf in stat.Influences) {
                                inf.IsDirty = true;
                                inf.IsSourcesDirty = true;
                                inf.Apply();
                            }
                        }
                    }
                }
            }
        }
    }
}

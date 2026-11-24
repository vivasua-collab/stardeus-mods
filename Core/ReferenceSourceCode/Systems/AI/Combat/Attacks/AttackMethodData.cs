using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.Systems.Stats;
using KL.Utils;
using Unity.Mathematics;
using System.Text;
using Game.Input;

namespace Game.Systems.AI.Combat {
    public struct AttackMethodData {
        public float DPS;
        public float ChargeSec;
        public float CooldownSec;
        /// <summary>
        /// Speed multiplier during attack. 0f = no multiplier. 0.01f - 1f - slower to faster
        /// </summary>
        public float MoveSpeedMul;
        public float StunChanceBio;
        public float StunChanceRobo;
        public float AccuracyMax;
        public float RangeMin;
        public float RangeMax;
        public Curve AccuracyCurve;
        public float BleedChance;
        public float InternalHitChanceMul;
        public float FatalHitChanceMul;
        // Sharp attacks deal no internal organ damage by design (see DamageType.cs) - this opts
        // a single Sharp attack method into the same internal-hit formula Blunt uses.
        public bool DeepCut;
        // Damage multiplier against robotic body parts (the Ion Disruptor), 0 or 1 = none
        public float RoboticDamageMul;
        // Share of the target's worn armor a hit ignores, 0-1: what gives the top weapons a
        // purpose against the top armor
        public float ArmorPenetration;
        public string Id;
        public string NameT;
        public DamageType Type;
        public string AttackAction;
        public string ChargeVFX;
        public string AttackVFX;
        public string ChargeSFX;
        public string AttackSFX;
        public string[] AttackSounds;
        // the same test as AttackMethod.IsMelee (RangeMax 0 means melee reach)
        public bool IsMeleeRange => RangeMax < 2f;
        public void ApplyToStats(StatsAttack stats, float quality) {
            stats.Quality.UpdateBase(quality);
            stats.DPS.UpdateBase(DPS);
            stats.DPS.Apply();
            stats.ChargeSec.UpdateBase(ChargeSec);
            stats.CooldownSec.UpdateBase(CooldownSec);
            stats.MoveSpeedMul.UpdateBase(MoveSpeedMul);
            stats.StunChanceBio.UpdateBase(StunChanceBio);
            stats.StunChanceRobo.UpdateBase(StunChanceRobo);
            stats.AccuracyMax.UpdateBase(AccuracyMax);
            stats.AccuracyMax.Apply();
            stats.RangeMin.UpdateBase(RangeMin);
            // if rangeMax is 0, it's a melee attack, let's set it to 1.5
            if (RangeMax == 0f) {
                RangeMax = 1.7f;
            }
            stats.RangeMax.UpdateBase(RangeMax);
            stats.RangeMax.Apply();
            stats.BleedChance.UpdateBase(BleedChance);
            stats.ArmorPenetration.UpdateBase(ArmorPenetration);
        }
        public void ApplyTo(AttackMethod meth, float quality) {
            meth.Id = Id;
            meth.NameT = NameT;
            meth.Action = Registries.AttackActions.Get(AttackAction);
            meth.AccuracyCurve = AccuracyCurve;
            meth.ChargeVFX = ChargeVFX;
            meth.AttackVFX = AttackVFX;
            meth.AttackSFX = AttackSFX;
            meth.ChargeSFX = ChargeSFX;
            meth.AttackSounds = AttackSounds;
            meth.FatalHitChanceMul = FatalHitChanceMul;
            meth.InternalHitChanceMul = InternalHitChanceMul;
            meth.DeepCut = DeepCut;
            meth.RoboticDamageMul = RoboticDamageMul > 0f ? RoboticDamageMul : 1f;
            meth.Type = Type;
            ApplyToStats(meth.Stats, quality);
            if (RangeMax < 2f) {
                meth.IsMelee = true;
            }
            if (AccuracyCurve != null) {
                float maxAcc = -1f;
                float maxAccDistPct = 0.5f;
                for (float i = 0f; i <= 1f; i += 0.05f) {
                    var acc = AccuracyCurve.Evaluate(i);
                    if (acc > maxAcc) {
                        maxAcc = acc;
                        maxAccDistPct = i;
                    }
                }
                // the curve spans RangeMin to RangeMax (AttackMethod.IsWithinAttackRange), so
                // its peak sits that far between them, not that share of RangeMax
                meth.MaxAccuracyDist = math.lerp(meth.Stats.RangeMin.Value,
                    meth.Stats.RangeMax.Value, maxAccDistPct);
                // walking in from the far end, the first sample past the peak that keeps half
                // of the peak accuracy (the AI repositions below that)
                float effectivePct = maxAccDistPct;
                for (int step = 20; step >= 0; step--) {
                    float t = step * 0.05f;
                    if (t <= maxAccDistPct) { break; }
                    if (AccuracyCurve.Evaluate(t) >= maxAcc * 0.5f) {
                        effectivePct = t;
                        break;
                    }
                }
                meth.EffectiveRangeMax = math.lerp(meth.Stats.RangeMin.Value,
                    meth.Stats.RangeMax.Value, effectivePct);
            } else {
                meth.MaxAccuracyDist = (meth.Stats.RangeMin.Value + meth.Stats.RangeMax.Value) * 0.5f;
                meth.EffectiveRangeMax = meth.Stats.RangeMax.Value;
            }
            meth.CooldownTicks = (int) math.round(Consts.TicksPerMinute * CooldownSec);
            meth.FullChargeTicks = (int) math.round(Consts.TicksPerMinute * ChargeSec);
            if (meth.FullChargeTicks <= 1) {
                D.Err("FullChargeTicks too short for attack method: {0}", D.DumpInstance(meth));
                meth.FullChargeTicks = 2;
            }
        }

        public static void AppendInfo(StringBuilder sb, float quality, bool shortList, AttackMethodData data, int num) {
            if (num > 0) {
                sb.AppendLine();
            }
            sb.AppendFormat("{0} {1}",Texts.Bold(data.NameT),
                Texts.GreyParentheses(
                    T.AtQuality_V1.F(Units.Percentage(quality, clamp: false))));
            var stats = new StatsAttack();
            stats.FinalizeStats();
            data.ApplyToStats(stats, quality);
            var wantedStats = shortList ? stats.ShortStats : stats.FullStats;
            foreach (var stat in wantedStats) {
                // a melee hit lands by the attacker's skill, the weapon has no accuracy to show
                if (stat == stats.AccuracyMax && data.IsMeleeRange) { continue; }
                // most weapons have none; a 0% line on every one would only be noise
                if (stat == stats.ArmorPenetration && stat.Value <= 0f) { continue; }
                sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, stat.StatString());
            }
            if (data.RoboticDamageMul > 0f && data.RoboticDamageMul != 1f) {
                var mul = Texts.Accented($"x{data.RoboticDamageMul:0.##}");
                sb.Append($"\n {GlyphMap.ArrowRight} {T.NounDamage} ({T.SpeciesTypeRobotic}): {mul}");
            }
        }

        public static AttackMethodData[] Deserialize(ComponentConfig[] data) {
            if (data == null) { return null; }
            var res = new List<AttackMethodData>(data.Length);
            for (int i = 0; i < data.Length; i++) {
                ComponentConfig cfg = data[i];
                // cfg.EnsureHash();
                var meth = new AttackMethodData();
                meth.Id = cfg.Component;
                meth.NameT = $"attack.method.{meth.Id}".T();
                meth.AttackAction = cfg.GetString(PropertyIdH.AttackAction);
                meth.DPS = cfg.GetFloat(PropertyIdH.DPS);
                meth.ChargeSec = cfg.GetFloat(PropertyIdH.ChargeSec);
                meth.CooldownSec = cfg.GetFloat(PropertyIdH.CooldownSec);
                meth.MoveSpeedMul = cfg.GetFloat(PropertyIdH.MoveSpeedMul, 0.05f);
                // melee attacks have none (set below); a ranged attack without one never hits
                meth.AccuracyMax = cfg.GetFloat(PropertyIdH.AccuracyMax, 0f);
                meth.StunChanceBio = cfg.GetFloat(PropertyIdH.StunChanceBio);
                meth.StunChanceRobo = cfg.GetFloat(PropertyIdH.StunChanceRobo);
                meth.RangeMin = cfg.GetFloat(PropertyIdH.RangeMin, 0f);
                meth.RangeMax = cfg.GetFloat(PropertyIdH.RangeMax, 0f);
                meth.BleedChance = cfg.GetFloat(PropertyIdH.BleedChance, 0f);
                var type = cfg.GetString(PropertyIdH.AttackType);
                if (type == null) {
                    D.Err("Misconfigured AttackMethod, no AttackType — skipping entry: {0}",
                        D.DumpInstance(cfg));
                    continue;
                }
                meth.Type = Enums.FromString<DamageType>(type);
                meth.AttackVFX = cfg.GetString(PropertyIdH.AttackVFX, null);
                meth.ChargeVFX = cfg.GetString(PropertyIdH.ChargeVFX, null);
                meth.AttackSFX = cfg.GetString(PropertyIdH.AttackSFX, null);
                meth.ChargeSFX = cfg.GetString(PropertyIdH.ChargeSFX, null);
                meth.AttackSounds = cfg.GetStringSet(PropertyIdH.AttackSounds, null);
                meth.InternalHitChanceMul = cfg.GetFloat(PropertyIdH.InternalHitChanceMul, 1f);
                meth.FatalHitChanceMul = cfg.GetFloat(PropertyIdH.FatalHitChanceMul, 1f);
                meth.DeepCut = cfg.GetBool(PropertyIdH.DeepCut, false);
                meth.RoboticDamageMul = cfg.GetFloat(PropertyIdH.RoboticDamageMul, 1f);
                meth.ArmorPenetration = cfg.GetFloat(PropertyIdH.ArmorPenetration, 0f);
                var accFloats = cfg.GetFloatSet(PropertyIdH.AccuracyCurve, null);
                if (!Arrays.IsEmpty(accFloats)) {
                    meth.AccuracyCurve = accFloats;
                }
                if (meth.IsMeleeRange) {
                    // A melee attack lands by the attacker's skill alone (MeleeAccuracy).
                    // Everything that weighs attacks by accuracy (weapon choice, attack priority,
                    // raid pricing) reads this typical hit chance instead
                    meth.AccuracyMax = Tunables.CombatMeleeReferenceHitChance;
                }
                res.Add(meth);
            }
            return res.Count > 0 ? res.ToArray() : null;
        }
    }
}

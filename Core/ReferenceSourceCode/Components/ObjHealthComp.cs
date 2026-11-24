using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Visuals;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class ObjHealthComp : BaseComponent<ObjHealthComp>, IStatProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ObjHealthComp());
        }

        public bool IsDestroyed => health <= 0;
        public bool IsDamaged => health < max;
        public float MaxHealth => max;
        public bool IsInvincible;
        public FactionDef Faction => null;
        private float max;
        private float health;
        public float Health => health;
        public float HealthPercent => health / max;
        private BFloat healthPercentB;
        public BFloat HealthPercentB => healthPercentB ??= new BFloat(HealthPercent);

        public string StatsCategoryName => T.Health;

        public int StatsOrder => 100;

        protected override void OnConfig() {
            max = Config.GetInt(PropertyIdH.Max, 100);
            IsInvincible = Config.GetBool(PropertyIdH.IsInvincible, false);
            health = max;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Health", health);
            data.SetBool("IsInvincible", IsInvincible);
        }

        protected override void OnLoad(ComponentData data) {
            health = data.GetFloat("Health", max);
            IsInvincible = data.GetBool("IsInvincible", IsInvincible);
        }

        public override string ToString() {
            return string.Format(" * ObjHealth: {0}/{1} ({2:0}%)",
                health, max, (health / (float) max) * 100);
        }

        public void RepairDamage(float amount) {
            health += amount;
            if (health > amount) {
                health = amount;
            }
            UpdateHealthBar();
        }

        public override void OnLateReady(bool wasLoaded) {
            if (health < 0f && !IsInvincible) {
                D.Err("LateReady for object with negative health. Removing: {0}", Obj);
                S.Clock.OnNextTick(() => {
                    if (Obj.IsActive) {
                        S.Objs.Destroy(Obj);
                    }
                });
                return;
            }
            if (wasLoaded) {
                UpdateHealthBar();
            }
        }

        public void TakeDamage(float amount, DamageType type) {
            if (IsInvincible || !entity.IsActive) { return; }
            var healthBefore = health;
            health -= amount;
            if (amount >= 1f) {
                DamageText.Show(Obj.Position, (int) amount, Obj.Size.x * 2,
                    Obj.Size.x * 0.1f, false);
            }
            if (healthBefore > 0f) {
                entity.Flammable?.ReactToDamage(amount, health, type);
            }
            Transmit(this, MsgIdH.TookDamage);
            if (health <= 0) {
                health = 0;
                Transmit(this, MsgIdH.HealthWentToZero);
                if (entity.IsActive) {
                    D.Log("Destroying: {0}", Obj);
                    S.Objs.Destroy(Obj, false);
                }
            }
            UpdateHealthBar();
        }

        private void UpdateHealthBar() {
            if (IsDestroyed || Obj.Graphics.IsHidden) {
                HideInfoBar();
            } else if (HealthPercent > 0 && HealthPercent < 0.99f) {
                ShowInfoBar(Consts.ColorHealthBar, HealthPercent,
                    Mathf.RoundToInt(health), Mathf.RoundToInt(max),
                    "health.bar.info");
            } else {
                HideInfoBar();
            }
            healthPercentB?.Set(HealthPercent);
        }


        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText2Progress>(life,
                    UIPrefabId.WContentProgress, block.Container, idx++);
            w.Load(IconId.CHealth, T.Health, 0f);
            HealthPercentB.Bind(life, (p) => {
                w.SetPercent(p, false);
                w.Item.SetText2(Units.XOutOfY(health, MaxHealth));
            });
        }
    }
}

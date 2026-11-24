using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.Path;
using Game.Systems.Nanobots;
using Game.Visuals;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class DamageableComp : BaseComponent<DamageableComp>,
            IHullIntegrityProvider, IRelocatable,
            IAIGoalProvider, INanobotCompatible,
            IStatProvider {

        public struct DamageableDamageComparer : IComparer<DamageableComp> {
            public int Compare(DamageableComp x, DamageableComp y) {
                var d1 = x.HealthPercent;
                var d2 = y.HealthPercent;
                if (d1 > d2) { return 1; }
                if (d2 > d1) { return -1; }
                return 0;
            }
        }
        public static readonly DamageableDamageComparer DamageComparer = default;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new DamageableComp());
        }


        private float max;
        private float quarterMax;
        private float threeQuartersMax;
        public float MaxHealth {
            get => max;
            set => max = value;
        }
        private float health;
        public float Health => health;
        public bool IsDamaged => health < max;
        public bool IsHeavilyDamaged => health < quarterMax;
        public bool IsSignificantlyDamaged {
            get {
                if (health < threeQuartersMax) {
                    #if !PRODUCTION_BUILD
                    if (threeQuartersMax > max) {
                        D.Err("ThreeQuartersMax was not refreshed!");
                        D.Warn("IsSigDmg: {0}<{1} == {2}", health, threeQuartersMax, health < threeQuartersMax);
                    } else if (health == 0f || threeQuartersMax == 0f) {
                        D.Err("Checking IsSignificantlyDamaged with uninitialized health of 3/4max: {0}", Entity);
                    }
                    #endif
                    return true;
                }
                return false;
            }
        }
        public bool IsOperational => health > quarterMax;
        public float HealthPercent => health / max;
        private BFloat healthPercentB;
        public BFloat HealthPercentB => healthPercentB ??= new BFloat(health / max);
        public bool IsDestroyed => health <= 0f;
        public float Integrity => HealthPercent;
        private float repairSpeed = 1f;
        private bool isParticlesAdded;
        public DamageType LastDamageType;
        // Burnt by an explosion, heat or energy since it was last whole: the tile shows soot
        // (SurfaceWear.cginc). Sticky, so a bullet after a blast does not wash the soot off.
        public bool IsScorched { get; private set; }
        public float LastDamageAmount;
        public ParticlesSys.Data SparkParticles;
        public void Repair(float amount, bool instant = false) {
            if (instant) {
                health = max;
            }
            RepairDamage(amount);
        }

        public void RepairDamage(float amount) {
            health += amount * repairSpeed;
            if (health >= max) {
                health = max;
                IsScorched = false;
                Transmit(this, MsgIdH.RepairedAllDamage);
                RemoveDamage();
            } else {
                ShowInfoBar(Consts.ColorHealthBar, HealthPercent,
                    Mathf.RoundToInt(health), Mathf.RoundToInt(max), "health.bar.info");
            }
            UpdateDamageFX();
            Transmit(this, MsgIdH.RepairedSomeDamage);
            S.Sig.TileRepaired.Send(Tile);
            healthPercentB?.Set(HealthPercent);
        }

        public bool CreateRepairGoal() {
            if (HasGoal) { return false; }
            if (HasAnyGoal) {
                AIGoal.Cancel(entity.Goal, G.Ticks, T.Repair);
            }

            var repairGoal = CreateGoal(JobTypeIdH.Repairs,
                    IconId.CWrench, T.Repair, this)
                .WithOverwritePriority(-1)
                .WithPathFlags(PathReqFlags.AdjNearby)
                .NanobotCompatible()
                .ByPlayer(true)
                .WithWorldToDesired(AIVarsH.IsDamaged, true, false)
                .WithRequiredAbilities(AbilityIdH.Build, AbilityIdH.SkilledLabor)
                .WriteInitialState();
            return entity.TrySetGoal(repairGoal);
        }


        public void ChangeMaxHealth(float max) {
            var pctNow = HealthPercent;
            this.max = max;
            health = max * pctNow;
            quarterMax = max * 0.25f;
            threeQuartersMax = max * 0.75f;
            healthPercentB?.Set(HealthPercent);
        }

        // Wear follows the grids and health on its own (TileWearBuffer)
        public void RemoveDamage() {
            isDamageShown = false;
            RemoveParticles();
            HideInfoIcon();
            HideInfoBar();
        }

        public override void OnRemove() {
            RemoveDamage();
        }

        private void RemoveParticles() {
            if (isParticlesAdded) {
                isParticlesAdded = false;
                S.Sys.Particles.RemoveDamaged(this);
            }
        }

        private bool isDamageShown;

        private void RefreshWear() {
            S?.Sys?.Areas?.Wear?.Refresh(Tile);
        }

        public void UpdateDamageFX() {
            RefreshWear();
            if (HealthPercent > 0.999f) {
                if (isDamageShown) {
                    isDamageShown = false;
                    HideInfoBar();
                    RemoveParticles();
                }
                return;
            }
            isDamageShown = true;

            if (!isParticlesAdded && Tile.ENode != null) {
                isParticlesAdded = true;
                S.Sys.Particles.AddDamaged(this);
            }
        }

        public void SetHealth(float amt) {
            health = amt;
            if (health >= max) {
                IsScorched = false;
            }
            healthPercentB?.Set(HealthPercent);
            RefreshWear();
        }

        // Kept for mods compiled against the signature without autoRebuild
        public void TakeDamage(float amount, DamageType type, bool tryAutoRepair) {
            TakeDamage(amount, type, tryAutoRepair, true);
        }

        public void TakeDamage(float amount, DamageType type, bool tryAutoRepair = true,
                bool autoRebuild = true) {
            // D.Warn("taking damage: {0} {1}", amount, type);
            if (IsDestroyed) {
                return;
            }
            if (entity == null) {
                D.Err("Entity is null in DamageableComp!");
                return;
            }
            if (Tile == null) {
                D.Err("Tile is null in DamageableComp! {0}", entity);
                return;
            }
            if (Tile.Transform == null) {
                D.Err("Tile.Transform is null in DamageableComp! {0}", Tile);
                return;
            }
            if (Tile.Transform.IsChild) {
                D.Err("Trying to deal damage to a child tile: {0}. Parent: {1}",
                    Tile, Tile.Transform.Parent);
                return;
            }
            if (!Tile.IsConstructed) {
                if (Ready.Gen) {
                    D.Err("Trying to deal damage an unconstructed tile: {0}. Dmg: {1}, Constr: {2}",
                        Tile, this, Tile.Constructable);
                }
                return;
            }
            if (S == null) {
                D.Err("S is null in DamageableComp!");
                return;
            }
            if (S.CmdQ == null) {
                D.Err("S.CMDq is null in DamageableComp!");
                return;
            }
            if (!GameState.IsCurrent(S)) {
                D.Err("Runtime is not current in DamageableComp!");
                return;
            }
            if (entity.IsImmutable) {
                return;
            }
            if (!Ready.Gen) {
                //InvincibleParts is readonly
                if (S.Map?.ShipSpec?.InvincibleParts.Contains(entity.PosIdx) == true) {
                    // // D.Log("Avoiding damage at {0}", entity.PosIdx);
                    // Invicible during gen
                    return;
                }
            }
            LastDamageType = type;
            LastDamageAmount = amount;
            if (type == DamageType.Explosive || type == DamageType.Heat
                    || type == DamageType.Energy) {
                IsScorched = true;
            }
            if (amount >= 1f) {
                DamageText.Show(Tile.Position, (int) amount,
                    4f, 0.1f, false);
            }

            health -= amount;
            // D.Log("Taking damage: {0}/{1}. Health now: {2}. {3}", amount, type, health, Entity);
            healthPercentB?.Set(HealthPercent);

            if (HealthPercent <= 0.999f && HealthPercent > 0) {
                UpdateDamageFX();
            }

            AdjustTension(amount);

            if (entity.Flammable?.ReactToDamage(amount, health, type) == true) {
                // entity was removed
                return;
            }
            if (health <= 0) {
                entity.IsInfoDisabled = true;
                // TODO trigger some destruction effects, if it's an explosive
                // object, explode it
                var flyRadius = Mathf.Min(Mathf.Sqrt(amount) * 0.5f, 3f);
                // A floor marked for deconstruction is not rebuilt, so whatever stood on it
                // must not be either - it would be a blueprint with no floor to stand on.
                // Read before BreakDown, which clears the goal.
                var rebuildAbove = autoRebuild && Tile.Goal?.JobIdH != JobTypeIdH.Demolition;
                if (Tile.Constructable != null) {
                    // This was commented out - why?
                    Transmit(this, MsgIdH.HealthWentToZero);
                    Tile.Constructable.BreakDown(flyRadius, autoRebuild: autoRebuild, immediate: true);
                } else {
                    D.Err("Tile has no constructable! {0}", entity);
                    S.CmdQ.Enqueue(new CmdRemoveTile(Tile));
                    // Lags in generation
                    //new CmdRemoveTile(entity.Pos, LayerId).Execute();
                }

                // We should see if it's a floor, and then if it is, remove any
                // objects above
                if (LayerId != WorldLayer.Floor) { return; }

                var wall = EntityUtils.WallAt(entity.PosIdx);
                if (wall?.IsActive == true) {
                    wall.Constructable?.BreakDown(flyRadius, autoRebuild: rebuildAbove, immediate: true);
                } else {
                    var obj = EntityUtils.ParentObjAt(entity.PosIdx);
                    if (obj?.IsActive == true && !obj.IsImmutable) {
                        if (obj.Constructable == null) {
                            D.Err("Obj has no constructable {0}", obj);
                        } else {
                            obj.Constructable.BreakDown(flyRadius, autoRebuild: rebuildAbove, immediate: true);
                        }
                    }
                }
            } else {
                Transmit(this, MsgIdH.TookDamage);
                if (tryAutoRepair) {
                    TryAutoRepair();
                }
            }
        }

        public void TryAutoRepair() {
            if (!Ready.Gen) { return; }
            if (!S.Research.IsUnlocked(DefIdsH.ResearchRoboticsAutoRepair)) {
                return;
            }
            if (!EntityUtils.IsWithinMargin(entity.PosIdx, false, false)) {
                return;
            }
            if (Tile.HasComponent<BreachCapsuleComp>()) {
                return;
            }
            var g = entity.Goal;
            if (g != null) {
                if (g.Target == this) {
                    // we have a repair goal
                    return;
                }
                if (g.JobIdH == JobTypeIdH.Demolition) {
                    // don't overwrite deconstruct task
                    return;
                }
            }

            if (S.Sys.Combat?.IsHostileNear(entity.PosIdx, Tunables.AutoRepairDangerRadius) == true) {
                // Don't send colonists wandering into a firefight to patch a door
                // that's still being shot at. AutoRepairSys sweeps once combat ends.
                return;
            }

            if (!CreateRepairGoal()) { return; }
            entity.Goal.WithPriority(A.AutoRepairPriority);
        }

        private void AdjustTension(float amount) {
            if (!Ready.Gen) { return; }
            var dmgScale = amount / max;
            if (dmgScale > 1f) {
                dmgScale = 1f;
            }

            if (dmgScale > 0f) {
                var size = Tile.Transform.CellCount;
                if (S.Situation == null) {
                    D.Err("S.Situation is null in DamageableComp!");
                } else {
                    // S.Situation.AddTension(
                        // 0.01f + dmgScale * 0.01f * Mathf.Sqrt(size),
                        // "Tile taking damage");
                }
            }
        }

        protected override void OnConfig() {
            repairSpeed = Tunables.RepairSpeed;

            max = Config.GetInt(PropertyIdH.Max, 0);
            if (Tile == null) {
                D.Err("Damageable component created on something that is not a Tile! {0}",
                    Entity);
            } else if (max == 0) {
                var ttcfg = Tile.Definition.ComponentConfigFor(ComponentIdH.TileTransform);
                if (ttcfg == null) {
                    max = 100;
                } else {
                    var w = ttcfg.GetInt(PropertyIdH.Width, 1);
                    var h = ttcfg.GetInt(PropertyIdH.Height, 1);
                    var size = Mathf.Max(w, h);
                    if (size > 1 && (w > 1 && h > 1)) {
                        size++;
                    }
                    max = 100 * size;
                }
            }
            health = max;
            quarterMax = max * 0.25f;
            threeQuartersMax = max * 0.75f;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Health", health);
            data.SetBool("FullHealth", Maths.Approximately(health, max));
            if (IsScorched) {
                data.SetBool("Scorched", true);
            } else {
                data.RemoveBool("Scorched");
            }
        }

        protected override void OnLoad(ComponentData data) {
            health = data.GetFloat("Health", max);
            IsScorched = data.GetBool("Scorched", false);
            // Migration for old saves
            if (!Maths.Approximately(health, max)) {
                if (data.GetBool("FullHealth", true)) {
                    health = max;
                } else {
                    if (health > max) {
                        health = max;
                    }
                }
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            var size = Tile.Transform.Width * Tile.Transform.Height;

            if (wasLoaded) {
                D.Ass(Data != null, "Data null at load time (damageable)");
                if (HealthPercent < 0.999f) {
                    UpdateDamageFX();
                }
            }
        }

        public override string ToString() {
            return string.Format(" * Damageable: {0}/{1} ({2:0}%)",
                health, max, HealthPercent * 100);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.TilePosChanged) {
                if (isDamageShown) {
                    RemoveDamage();
                    UpdateDamageFX();
                }
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (SparkParticles != null) {
                SparkParticles.Position += new Vector2(dx, dy);
                SparkParticles.EmitParams.position += new Vector3(dx, dy, 0);
            }
        }


        public void RelocateTo(Entity target) {
            var dmgbl = target.GetComponent<DamageableComp>();
            dmgbl.health = health;
            dmgbl.IsScorched = IsScorched;
            dmgbl.UpdateDamageFX();
            dmgbl.healthPercentB?.Set(dmgbl.HealthPercent);
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public void AddNanobotsEffort(float amt, out AIGoal goal) {
            if (!TryGetGoal(out goal)) { return; }
            RepairDamage(amt);
            if (!IsDamaged) {
                goal.ChangeStateTo(AIGoalState.Completed, G.Ticks);
            }
        }

        public string StatsCategoryName => T.Health;
        public int StatsOrder => -20;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress,
                block.Container, idx++);
        w.Load(IconId.CHealth, T.Health, 0f);
            HealthPercentB.Bind(life, (p) => {
                w.SetPercent(p, false);
                w.Item.SetText2(Units.XOutOfY((int) health, (int) max));
            });
        }
    }
}

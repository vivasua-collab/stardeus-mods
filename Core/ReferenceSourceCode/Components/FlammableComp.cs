#if UNITY_EDITOR
// #define DEBUG_EXPLOSIVENESS
// #define DEBUG_FIRE
#endif
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Game.Systems.Firex;
using Unity.Mathematics;
using Game.Mods;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Systems.Notify;
using Game.Systems.Voice;
using Game.Systems.Story;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class FlammableComp : BaseComponent<FlammableComp>,
            IStatProvider, IThreat {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new FlammableComp());
            LastOnFireLogTime = 0d;
        }
        private static double LastOnFireLogTime;

        // Flammable idx
        public int SlotIdx;
        private FirexSys sys;
        public Flammable Flam;
        public bool HasExploded;
        // x axis is health %, y axis is % chance to explode when taking damage
        private Curve explCurve;
        public Curve ExplosivenessCurve {
            get {
                var curve = expProvider?.ExplosivenessCurve ?? explCurve;
                if (curve == null) {
                    D.Err("Explosion curve is null for {0} (expl provider: {1})",
                        Entity.Definition.Id, expProvider);
                    curve = explCurve = CurveDef.FastGet(CurveIdH.Explosiveness_Device);
                }
                return curve;
            }
        }
        private IExplosivenessProvider expProvider;
        // x axis is health %, y axis is % chance to self ignite when taking damage
        public Curve FlammabilityCurve;
        public bool IsOnFire;
        private bool isAddedToSec;
        private float selfCombustTempKelvin;
        public float SpreadIntoChance;
        public float ChanceToCatchOnFire;
        public bool AffectsSoundtrack => IsWorthExtinguishing;

        public bool IsWorthExtinguishing {
            get {
                var pos = entity.PosIdx;
                // no oxygen
                if (S.GetO2(pos) < 0.01f) { return false; }
                if (!EntityUtils.IsWithinMargin(entity.PosIdx, false, false)) {
                    // derelict ship, don't care
                    return false;
                }
                if (Being?.Data.IsFriendly == false && Being.CanFight) {
                    // enemy on fire that can still fight, ignore
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 0 - all damage goes through, 1 - no damage
        /// </summary>
        public float FireDamageResistance;
        public bool IsActive;
        private bool canExplode;
        public bool CanExplode => canExplode || expProvider?.ExplosionLevel > 0;
        /// <summary>
        /// 0 = No blast radius, only damages self,
        /// 2 = cooking station explosion
        /// 5 = large matter reactor explosion
        /// 7 = fusion drive explosion
        /// 10 = big nuclear explosion taking down floor with it
        /// </summary>
        /// <seealso cref="IExplosivenessProvider"/>
        private int baseExplosionLevel;
        public int ExplosionLevel => baseExplosionLevel + (expProvider?.ExplosionLevel ?? 0);
        public bool CanCatchOnFireWhenDamaged;
        public long CanSelfCombustAfter;
        // A being a firefoam spray covered cannot catch fire before this tick
        // (FirefoamBurst); foam on the floor only protects what is not alive
        public long FoamCoverUntil;
        public bool IsTemperatureResistant;
        private NotificationHandle fireEvent;
        public float Health {
            get {
                if (Tile != null) {
                    return Tile.Health.Health;
                } else if (Obj != null) {
                    return Obj.Health.Health;
                }
                // FIXME beings do not use health like tiles / objs do
                return 100f;
            }
        }


        private void CreateFireEvent() {
            if (!Ready.Gen) { return; }
            if (Being == null && !EntityUtils.IsWithinMargin(Entity.PosIdx, false, false)) {
                return;
            }
            if (Being != null) {
                Being.Data.SetAvatarAnimatedSprite(IconId.CFire);
            }
            if (S.Situation.TimesOnFire <= S.Scenario.FiresToIgnore) { return; }
            string title;
            if (Tile != null) {
                title = T.FireAt_V1.F(Pos.String(Tile.CenterIdx));
            } else if (Obj != null) {
                title = $"{T.OnFire}: {Obj.EntityName}";
            } else if (Being != null) {
                title = $"{T.OnFire}: {Being.EntityName}";
            } else {
                title = T.Fire;
            }
            fireEvent = Notification.BuildFor(entity, IconId.CFire, title,
                Consts.TicksPer8Hours, NotificationType.Critical)
                .Emergency()
                .Group("fire")
                .WithVoice(VoiceLine.Fire)
                .Show();
            if (S.Sys.Firex.ExtinguishableFireCount <= 1) {
                var rt = Time.realtimeSinceStartupAsDouble;
                // don't spam
                if (LastOnFireLogTime > rt || LastOnFireLogTime < rt - 10) {
                    LastOnFireLogTime = rt;
                    S.Sys.Log.AddLine(Entity, Texts.Red(T.OnFire));
                }
            }
        }
        private void RemoveFireEvent() {
            if (Being != null) {
                Being.Data.SetAvatarAnimatedSprite(null);
            }
            if (!fireEvent.IsValid) { return; }
            Notification.Remove(ref fireEvent);
            S.Sys.Log.AddLine(Entity, Texts.Green(T.FireExtinguished));
        }

        public override void OnRemove() {
            RemoveThreat();
            if (isRegistered) {
                if (Being != null && Being.Data != null) {
                    if (Being.Data.HasCondition(ConditionIdH.OnFire)) {
                        Being.Data.RemoveCondition(S, ConditionIdH.OnFire);
                    }
                }
                UnregisterFlam();
            }
            // keeps FirexSys.FlammablesOnFire exact when an unregistered one burned
            SetOnFire(false);
        }

        private bool isRegistered;
        private void AddThreat() {
            if (!isAddedToSec && IsThreatActive) {
                isAddedToSec = true;
                S.Sig.RegisterThreat.Send(this, true);
            }
        }
        private void RemoveThreat() {
            if (isAddedToSec) {
                isAddedToSec = false;
                S.Sig.RegisterThreat.Send(this, false);
            }
        }

        private void UnregisterFlam() {
            if (isRegistered) {
                isRegistered = false;
            } else {
                D.Err("Trying to unregister flammable more than once! {0} -> {1}", this, Entity);
                return;
            }
            #if DEBUG_FIRE
            D.Log("Unregistering flammable: {0} {1} (active: {2})",
                EntityId, SlotIdx, IsActive);
            #endif
            RemoveThreat();
            if (!IsActive) { return; }
            sys.RemoveFlammable(this);
        }

        protected override void OnConfig() {
            IsTemperatureResistant = Config.GetBool(
                PropertyIdH.IsTemperatureResistant, false);
            // How likely will the tile self combust OR receive a flame from the neighbor
            // this does not affect the self combustion when taking damage
            ChanceToCatchOnFire = Config.GetFloat(PropertyIdH.FireChance,
                Tunables.FireChanceDefault);

            // Some stuff won't receive much damage when burning (i.e. steel structure)
            // Explosives will receive damage 100% of the time if they are burning
            FireDamageResistance = Mathf.Clamp01(Config.GetFloat(PropertyIdH.FireDamageResistance,
                Tunables.FireDamageResistanceDefault));

            var flamCurveId = Config.GetString(PropertyIdH.FlammabilityCurve, null);
            if (flamCurveId != null) {

                // Handle legacy
                if (flamCurveId.StartsWith("FlammabilityCurve_")) {
                    var err = string.Format("FlammabilityCurve_ prefix is obsolete, remove it! {0} | {1}",
                        entity.DefinitionId, flamCurveId);
                    D.Err(err);
                    flamCurveId = flamCurveId.Replace("FlammabilityCurve_", string.Empty);
                }

                if (CurveDef.TryGet($"Flammability/{flamCurveId}", out FlammabilityCurve)) {
                    CanCatchOnFireWhenDamaged = true;
                } else {
                    var err = string.Format("Could not find FlammabilityCurve in Curves/Flammability/ by id: {0} for {1}",
                        flamCurveId, entity.DefinitionId);
                    D.Err(err);
                    ModRegistry.WarnAboutBrokenMod(entity, err);
                    CanCatchOnFireWhenDamaged = false;
                }
            } else {
                CanCatchOnFireWhenDamaged = false;
            }

            var explCurveId = Config.GetString(PropertyIdH.ExplosivenessCurve, null);
            if (explCurveId != null) {
                // Handle legacy
                if (explCurveId.StartsWith("ExplosivenessCurve_")) {
                    var err = string.Format("ExplosivenessCurve_ prefix is obsolete, remove it! {0} | {1}",
                        entity.DefinitionId, explCurveId);
                    D.Err(err);
                    explCurveId = explCurveId.Replace("ExplosivenessCurve_", string.Empty);
                }

                if (CurveDef.TryGet($"Explosiveness/{explCurveId}", out explCurve)) {
                    canExplode = true;
                } else {
                    var err = string.Format("Could not find ExplosivenessCurve in Curves/Explosiveness/ by id: {0} for {1}",
                        explCurveId, entity.DefinitionId);
                    D.Err(err);
                    ModRegistry.WarnAboutBrokenMod(entity, err);
                    canExplode = false;
                }
            } else {
                canExplode = false;
            }

            if (CanExplode) {
                baseExplosionLevel = Config.GetInt(PropertyIdH.BaseExplosionLevel, -1);
                if (baseExplosionLevel == -1) {
                    D.Err("FlammableComp has not defined BaseExplosionLevel for entity that can explode: {0}. Explosion provider: {1}",
                        Entity.Definition.Id, expProvider);
                }
            } else {
                Config.MarkUsed(PropertyIdH.BaseExplosionLevel);
            }

            var ignitionTemperatureCelsius = Config.GetInt(
                PropertyIdH.IgnitionTemperatureCelsius,
                Tunables.SelfCombustTempCelsiusDefault);

            // Cant self combust at 0 celsius or lower anyway, so will
            // not self combust at all if ignition temp is set to 0 or lower
            if (ignitionTemperatureCelsius <= 0) {
                selfCombustTempKelvin = 0;
            } else {
                selfCombustTempKelvin = Units.CelsiusToKelvin(
                    ignitionTemperatureCelsius);
            }

            SpreadIntoChance = Config.GetFloat(PropertyIdH.SpreadIntoChance,
                Tunables.FireSpreadIntoChanceDefault);
        }

        public override void OnReady(bool wasLoaded) {
            sys = S.Sys.Firex;
        }

        private void AddFlammable() {
            if (isRegistered) {
                D.Err("Flammable already registered {0} -> {1}", this, Entity);
                return;
            }
            isRegistered = true;
            // register this node with the FirexSys
            #if DEBUG_FIRE
            D.Log("Adding flammable: {0} (registered: {1})",
                EntityId, IsRegistered);
            #endif
            sys.AddFlammable(this);
            if (Obj != null) {
                Obj.Transform.OnPositionChange += OnObjPosChange;
            } else if (Being != null) {
                Being.Motion.OnPositionChange += OnBeingPosChange;
            }
        }

        private void OnBeingPosChange(Being b, int _2, int posTo) {
            if (!IsActive) { return; }
            if (!isAddedToSec && IsThreatActive) {
                AddThreat();
            }
            Flam.PosIdx = posTo;
            sys.Update(this, Flam);
        }

        private void OnObjPosChange(Obj _, int _2, int posTo) {
            if (!IsActive) { return; }
            if (!isAddedToSec && IsThreatActive) {
                AddThreat();
            }
            Flam.PosIdx = posTo;
            sys.Update(this, Flam);
        }

        public override void OnLateReady(bool wasLoaded) {
            // Assign the explosiveness provider
            for (int i = Entity.Components.Length - 1; i >= 0; i--) {
                var comp = Entity.Components[i];
                if (comp is IExplosivenessProvider expl && !expl.IsExplosivenessProviderDisabled) {
                    if (expProvider == null) {
                        expProvider = expl;
                    } else {
                        D.Err("More than one IExplosivenessProvider on {0}: {1} + {2}! You must disable all explosiveness providers except one in OnConfig!",
                            Entity.Definition.Id, expProvider, expl);
                    }
                }
            }
            if (!wasLoaded && !Config.GetBool(PropertyIdH.SkipExplosivenessValidation, false)) {
                ValidateExplosiveness();
            }
            if (Tile != null && !Tile.IsConstructed) {
                // D.Warn("Won't register flammable, tile not constructed: {0}", Tile);
                return;
            }
            if (!IsActive && !wasLoaded) {
                AddFlammable();
            }
            if (wasLoaded) {
                if (CanSelfCombustAfter != 0L && CanSelfCombustAfter < G.Ticks) {
                    CanSelfCombustAfter = 0L;
                }
            }
        }

        public Flammable LoadFlammable(in Flammable flam) {
            if (flam.EntityId != EntityId) {
                D.Err("Entity id mismatch in LoadFlammable! {0}->{1}", EntityId, flam);
            }
            isRegistered = true;
            Flam.RestoreFromSaved(flam);
            SetFlammableParams(ref Flam);
            SetSlot(Flam.Idx);
            // can't update here, too early
            // Update(flam);
            SetOnFire(flam.IsOnFire);
            return Flam;
        }

        // Every IsOnFire change in game code goes through here, so FirexSys can tell when
        // nothing at all is burning
        internal void SetOnFire(bool onFire) {
            if (IsOnFire == onFire) { return; }
            IsOnFire = onFire;
            S.Sys.Firex.FlammablesOnFire += onFire ? 1 : -1;
        }
        public void SetSlot(int idx) {
            SlotIdx = idx;
            IsActive = SlotIdx >= 0;
            if (isRegistered != IsActive) {
                D.Err("Flammable registed != active! {0} != {1}. {2} -> {3}",
                    isRegistered, IsActive, this, Entity);
            }
            #if DEBUG_FIRE
            D.Log("Setting slot on flammable: {0} idx:{1} (registered: {2})",
                EntityId, idx, IsRegistered);
            #endif
        }
        public override void Receive(IComponent sender, int message) {
            // When damaged, disconnect or update node stuff (status)
            if (message == MsgIdH.TilePosChanged) {
                OnObjPosChange(null, Pos.Invalid, Tile.PosIdx);
            } else if (message == MsgIdH.ConstructionFinished) {
                if (Entity.IsActive && Tile != null) {
                    AddFlammable();
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                if (Being == null) {
                    if (isRegistered) {
                        UnregisterFlam();
                    }
                    //sys.RemoveFlammable(this);
                }
            }
        }

        public void SetFlammableParams(ref Flammable flam) {
            flam.IsWall = entity.Definition.LayerId == WorldLayer.Walls;
            flam.PosIdx = entity.PosIdx;
            flam.IsTempResistent = IsTemperatureResistant;
            flam.EntityId = EntityId;
            flam.SelfCombustTempKelvin = selfCombustTempKelvin;
            // if (spreadIntoTempKelvin > 0) {
            //     D.Warn("Spread temp for {0}: {1} (self combust: {2})",
            //         Entity.Definition.Id,
            //         Texts.Temperature(flam.SpreadIntoMinTempKelvin),
            //         Texts.Temperature(flam.SelfCombustTempKelvin));
            // }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            if (CanSelfCombustAfter != 0) {
                data.SetLong("SelfCombTick", CanSelfCombustAfter);
            } else {
                data.RemoveLong("SelfCombTick");
            }
            if (FoamCoverUntil != 0) {
                data.SetLong("FoamCover", FoamCoverUntil);
            } else {
                data.RemoveLong("FoamCover");
            }
        }

        protected override void OnLoad(ComponentData data) {
            CanSelfCombustAfter = data.GetLong("SelfCombTick", 0L);
            FoamCoverUntil = data.GetLong("FoamCover", 0L);
        }

        public void Update(in Flammable flam) {
            if (!IsOnFire && flam.IsOnFire) {
                if (!IsActive) {
                    if (!entity.IsActive) {
                        #if !PRODUCTION_BUILD
                        D.ErrSoft("Skipping marking of inactive entity as on fire {0} -> {1}",
                            this, flam);
                        #else
                        D.Warn("Skipping marking of inactive entity as on fire {0} -> {1}",
                            this, flam);
                        #endif
                        return;
                    }
                    D.Err("Setting an unregistered flam on fire! {0} -> {1}",
                        this, flam);
                }
                CreateFireEvent();
                // D.Warn("Making flammable on fire: {0} | {1}", Entity, this);
                SetOnFire(true);
                AddThreat();
                if (Being != null) {
                    if (!Being.Data.HasCondition(ConditionIdH.OnFire)) {
                        Being.Data.AddCondition(S, new ConditionState() {
                            Condition = Registries.Conditions.FastGet(ConditionIdH.OnFire),
                            Severity = Severity.Mild
                        });
                    }
                }
            } else if (IsOnFire && !flam.IsOnFire) {
                //D.Warn("Marking flammable not on fire: {0}", Entity);
                SetOnFire(false);
                CanSelfCombustAfter = G.Ticks + Consts.TicksPer12Hours;
                RemoveThreat();
                RemoveFireEvent();
                if (Being != null) {
                    Being.Data.RemoveCondition(S, ConditionIdH.OnFire);
                }
            }
            Flam = flam;
        }

        public int ThreatType => ThreatIdH.Fire;

        public bool IsThreatActive => IsOnFire && IsWorthExtinguishing;

        public float AdjustFireSpreadWeight(float weight) {
            if (IsTemperatureResistant) { return 0f; }
            return weight * ChanceToCatchOnFire;
        }

        public bool CreateExplosion(bool destroySelf) {
            if (HasExploded) { return false; }
            expProvider?.OnExplode();
            var pos = entity.Position;
            var source = NamedEntity.For(Texts.LinkTo(T.Explosion, entity.CenterIdx));
            if (destroySelf && Entity.IsActive) {
                if (Tile != null) {
                    var autoRebuild = Ready.Gen;
                    var destroyCmd = new CmdRemoveTile(Tile, false,
                        autoRebuild: autoRebuild);
                    destroyCmd.Execute(S);
                } else if (Being != null) {
                    BeingUtils.Kill(S, Being.Data, DamageType.Explosive, source,
                        null, (slot) => {
                            return !slot.IsInternal;
                        }, 0);
                    S.Beings.Destroy(Being);
                    // Being.Health.EliminateWithoutTrace(true);
                } else if (Obj != null) {
                    S.Objs.Destroy(Obj, false);
                }
            }
            if (destroySelf) {
                HasExploded = true;
            }
            var expl = Explosion.Create(S, ExplosionLevel,
                position: pos,
                delay: 1);
            if (expl != null && Tile != null) {
                var posLink = Texts.LinkToPos(Tile.CenterIdx);
                var data = StoryEventData.CreateCustom(IconId.CExplosion, T.EvtDamagedDeviceExploded,
                    T.EvtDamagedDeviceExplodedDesc_V2.F(Tile.NameLink, posLink));
                data.TargetTile = Tile;
                S.Sig.AddCustomStoryEvent.Send(data, false);
                var explLine = $"{T.EvtDamagedDeviceExploded} ({T.TransformAt_V1.F(posLink)})";
                expl.BeforeExplode = () => {
                    S.Sys.Log.AddLine(Tile, Texts.Red(explLine), canRepeat: true);
                };
            }
            return destroySelf;
        }

        public float ObjectHealth {
            get {
                if (Tile != null) {
                    return Tile.Health.HealthPercent;
                } else if (Obj != null) {
                    return Obj.Health.HealthPercent;
                } else if (Being != null) {
                    // FIXME lame
                    if (Being.IsAlive) {
                        return 1f;
                    } else {
                        return 0f;
                    }
                }
                return 1f;
            }
        }

        internal static float AdjustFireOrExplosionChance(float chance, float dmgHealthPct) {
            // when we are estimating the chance to explode, we will further
            // reduce it by the amount of damage received
            // if it is ~1% of total health, the chance should be ~10x smaller
            // if it is 10% of total health, the chance should be 2x smaller
            // above 10% it will be untouched
            if (dmgHealthPct <= 0.1f) {
                // light blow
                var reduction = math.lerp(0.1f, 0.5f, dmgHealthPct * 10f);
                #if DEBUG_FIRE
                D.Warn("Reducing firex chance due to low damage % ({0}). {1} -> {2}",
                    dmgHealthPct, chance, chance * reduction);
                #endif
                chance *= reduction;
            } else if (dmgHealthPct >= 0.33f) {
                // heavy blow
                // if hit is > 33% health, we increase the chance of fire / explosion
                var increase = math.lerp(1f, 3f, dmgHealthPct);
                if (increase > 3f) {
                    increase = 3f;
                }
                #if DEBUG_FIRE
                D.Warn("Increasing firex chance due to low damage % ({0}). {1} -> {2}",
                    dmgHealthPct, chance, chance * increase);
                #endif
                chance *= increase;
                #if DEBUG_FIRE
            } else {
                D.Warn("On Damage with %: {0}", dmgHealthPct);
                #endif
            }
            return chance;
        }

        /// <returns>true if the object was destroyed</returns>
        public bool ReactToDamage(float dmgAmt, float curHealth, DamageType type) {
            if (!Ready.Gen) { return false; }

            if (CanExplode) {
                var chance = ExplosivenessCurve.Evaluate(ObjectHealth);
                if (chance > 0f) {
                    if (S.Situation.TimesOnFire <= S.Scenario.FiresToIgnore) {
                        #if DEBUG_FIRE
                        D.Warn("Skipping explosion chance {2} on damage - times on fire: {0} {1}",
                            S.Situation.TimesOnFire, Entity, chance);
                        #endif
                        return false;
                    }
                    var dmgHealthPct = dmgAmt / curHealth;
                    chance = AdjustFireOrExplosionChance(chance, dmgHealthPct);
                    if (Rng.UChance(chance)) {
                        if (CreateExplosion(true)) {
                            return true;
                        }
                    } else {
                        if (chance > 0f && Ready.Gen
                                && EntityUtils.IsWithinMargin(Entity.PosIdx, false, false)) {
                            S.Sys.Log.AddLine(entity,
                                T.ChanceToExplodeOnDmg_V1.F(Texts.Red(
                                    Units.PercentageWithFraction2(chance))));
                        }
                    }
                }
            }
            if (curHealth < 0.0001f) {
                // if curHealth was already negative, we will not interfere with the parent method
                return false;
            }
            if (DamageUtils.CanCauseFire(type)) {
                if (!IsOnFire && CanCatchOnFireWhenDamaged) {
                    var chance = FlammabilityCurve.Evaluate(ObjectHealth);
                    if (chance > 0f) {
                        var dmgHealthPct = dmgAmt / curHealth;
                        chance = AdjustFireOrExplosionChance(chance, dmgHealthPct);
                        if (Rng.UChance(chance)) {
                            S.Sys.Firex.TrySetOnFire(this, Fire.CreateBalanced(S));
                        }
                    }
                }
            } else if (DamageUtils.CanExtinguishFire(type)) {
                if (IsOnFire) {
                    S.Sys.Firex.AddFirefightingEffortFor(this, 0.2f);
                }
            }
            explodeOnDmgChanceB?.Set(CurrentExplodeOnDmgChance);
            // if curHealth was positive and in the end here entity is no longer active, we killed in this method
            // so return true;
            return curHealth > 0f && !entity.IsActive;
        }

        public float CurrentFireOnDmgChance => CanCatchOnFireWhenDamaged ?
            FlammabilityCurve.Evaluate(ObjectHealth) : 0f;
        public float CurrentExplodeOnDmgChance => CanExplode ?
            ExplosivenessCurve.Evaluate(ObjectHealth) : 0f;
        private BFloat explodeOnDmgChanceB;
        public BFloat ExplodeOnDmgChanceB => explodeOnDmgChanceB ??= new BFloat(CurrentExplodeOnDmgChance);


        public override string ToString() {
            return $"Flammable: [OnFire: {IsOnFire}][Registered:{IsActive}][{SlotIdx}] {entity}";
        }

        public bool TakeDamage(float dmg, DamageType type) {
            // we want to accumulate the chance of taking damage so
            // over time we will actually take it
            if (FireDamageResistance >= 1f) {
                return false;
            }
            dmg *= 1f - FireDamageResistance;
            if (Tile != null) {
                Tile.Health.TakeDamage(dmg, type);
            } else if (Obj != null) {
                Obj.Health.TakeDamage(dmg, type);
            } else if (Being != null) {
                return ConditionOnFire.DealFireDamageTo(Being.Data, dmg, G.Ticks);
            }
            return true;
        }

        private void ValidateExplosiveness() {
            if (!canExplode && expProvider != null && Tile != null) {
                D.Err("Flammable for tile {0} cannot explode but it has explosion provider! {1}",
                    Entity.Definition.Id, expProvider);
                canExplode = true;
            }
            if (Tile != null && expProvider == null) {
                int max = 0;
                foreach (var mat in Tile.Definition.ConstructedMats) {
                    if (mat.Type.ExplosionLevel > baseExplosionLevel) {
                        D.Err("FlammableComp of {0} has lower BaseExplosionLevel ({1}) than its construction material: {2} ({3})",
                            Tile.Definition.Id, baseExplosionLevel, mat.Type, mat.Type.ExplosionLevel);
                        max = mat.Type.ExplosionLevel;
                    }
                }
                if (max > baseExplosionLevel) {
                    baseExplosionLevel = max;
                    canExplode = true;
                }
            }
            if (Obj != null && Obj.Pile != null) {
                if (baseExplosionLevel != Obj.Pile.Type.ExplosionLevel) {
                    if (baseExplosionLevel > 0) {
                        D.Err("BaseExplosionLevel missmatch on Obj Def for mat: {0}: {1} vs {2} {3}",
                            Obj.Id, baseExplosionLevel, Obj.Pile.Type, Obj.Pile.Type.ExplosionLevel);
                    }
                    baseExplosionLevel = Obj.Pile.Type.ExplosionLevel;
                }
                if (baseExplosionLevel > 0) {
                    if (explCurve != null) {
                        D.Err("ExplosivenessCurve should not be set on raw material obj, it will be overwritten: {0}",
                            Obj.Definition.Id);
                    }
                    canExplode = true;
                    explCurve = Obj.Pile.Type.ExplosivenessCurve;
                }
            }
        }
        public string StatsCategoryName => T.Flammable;
        public int StatsOrder => 100;
        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var spread = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            spread.Load(IconId.CFire, T.FireSpreadChance, Units.Percentage(SpreadIntoChance));

            if (selfCombustTempKelvin > 0) {
                var ignition = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                ignition.Load(IconId.CTemperature, T.IgnitionPoint, Texts.Temperature(selfCombustTempKelvin));
            }

            if (CanExplode) {
                var explodeOnHit = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                explodeOnHit.Load(IconId.CExplosion, T.ExplodeOnHit, T.Yes);

                var chanceText = T.ChanceToExplodeOnDmg_V1.Replace("{0}", string.Empty).Replace(":", string.Empty);
                var chance = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
                chance.Load(IconId.CExplosion, chanceText, CurrentExplodeOnDmgChance);
                ExplodeOnDmgChanceB.Bind(life, (value) => {
                    chance.SetPercent(value, false);
                    chance.Item.SetText2(Units.PercentageColoredInverse(value, green: 0.05f, yellow: 0.15f));
                });

                var strength = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                strength.Load(IconId.CExplosion, T.ExplosionStrength, Units.Num(ExplosionLevel));
            }
        }
    }
}

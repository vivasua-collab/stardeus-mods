#if UNITY_EDITOR
// #define DEBUG_FIREX
// #define DISABLE_FIREX
#endif
#define FIREX_SAFE
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Burst;
using Game.Systems.Hints;
using Game.Commands;
using Unity.Mathematics;
using Game.Procgen;
using Game.VFXR;
using Game.Systems.Light;
using static Game.Systems.AudioSys;
using Game.Visuals;

namespace Game.Systems.Firex {

    /// <summary>
    /// Firex = Fire and Explosions
    /// </summary>
    public sealed class FirexSys : GameSystem, ISaveableSpecial {
        #if !PRODUCTION_BUILD
        public class FireComparer : EqualityComparer<Fire> {
            public override bool Equals(Fire x, Fire y) {
                return GetHashCode(x) == GetHashCode(y);
            }

            public override int GetHashCode(Fire obj) {
                return HashCode.Combine(obj.Idx, obj.EntityId, obj.FlamIdx);
            }
        }
        private static readonly FireComparer fireComparer = new();
        #endif
        public const string SysId = "FirexSys";
        public override string Id => SysId;
        [SelfInit]

        private static void Register() {
            GameSystems.Register(SysId, () => new FirexSys());
        }
        private VFXShardedParticleRenderer fireRend;
        private ReusableNativeArray<Flammable> arrFlams;
        private ReusableNativeArray<Fire> arrFires;
        // key is idx of arrFlams
        private readonly Dictionary<int, FlammableComp> flamComps = new();
        // key is idx of arrFlams
        public Dictionary<int, FlammableComp> FlamComps => flamComps;
        // key is idx of arrFires
        private readonly Dictionary<int, ParticleSlot> fireParticles = new();
        // key is idx of arrFires
        private readonly Dictionary<int, ushort> fireLights = new();
        // key is idx of arrFires
        private readonly Dictionary<int, AmbienceData> fireSounds = new();
        private const string ambienceId = "Ambience/Fire01";
        public Firefighting Firefight;
        public Explosions Explosions;
        public FireBoost Boost;
        private static readonly Vector2 ambienceRolloff = new(3, 15);
        private readonly static int ambienceIdHash =
            Hashes.S(ambienceId);
        public int OnFireCount;
        public int ExtinguishableFireCount;
        // Increments once every extinguishable fire has died. Flammables marked
        // with the current cycle cannot re-ignite until it advances, so nothing
        // burns twice within one fire incident
        public int FireCycle = 1;
        private bool hadExtinguishableFireLastTick;
        private bool hadAnyFireLastTick;
        private Curve fireHeatupCurveKelvin;
        public float FireO2ConsumptionRate;
        public float FireFuelConsumptionRate;
        public float FireChokeRate;
        public float FireStrengthAfterSpread;
        public float FireStrengthIncreaseRate;
        // Flammables are processed every 10 ticks
        public float FireMinDamagePerFlammableProcessCycle;
        public float FireMaxDamagePerFlammableProcessCycle;
        public float ColdChanceDamage;
        public float HeatChanceDamage;
        public float ColdDamagePer100Deg;
        public float HeatDamagePer100Deg;
        public float FireNewFlameStrengthAfterSpread;
        public float FireSpreadRate;
        public int FireMaxSpreadPerTick;
        public float FireSpreadToWallChance;
        public float FireStrengthAfterFailedSpread;
        public bool IsAnythingOnFire;
        // Flammables with IsOnFire set (FlammableComp.SetOnFire). Unlike OnFireCount it
        // includes weak fires, so 0 means no FlammableComp.IsOnFire is true, and per-goal
        // fire checks over thousands of colony goals can be skipped
        public int FlammablesOnFire;
        private const string particleVFXId = "VFXParticlesFire";
        // Change VFXParticlesFire if you change this value!
        private const int particleVFXShardSize = 32;
        // ReinitOnShow is false for the fire renderer (see VFXParticlesFire.vfx notes),
        // so we force a periodic reinit ourselves to avoid particles drifting/vanishing
        // after long continuous playtime. Real time, not game ticks, since VFX Graph
        // keeps simulating while the game clock is paused.
        private const double fireForceReinitIntervalSeconds = 60 * 60;
        private double lastFireForceReinitTime;

        private bool InitializeParticles() {
            if (fireRend != null) { return true; }
            var vfxIdH = Hashes.S(particleVFXId);
            fireRend = S.Sys.Rend.GetParticleRenderer(particleVFXId, vfxIdH,
                particleVFXShardSize);
            fireRend.ReinitOnShow = false;
            lastFireForceReinitTime = Time.realtimeSinceStartupAsDouble;
            return fireRend != null;
        }

        private bool AddFireAudioVisuals(Fire fire, out ParticleSlot slot) {
            if (!fire.IsValid) {
                D.Err("Fire is not initialized when trying to add particle! {0}",
                    D.DumpInstance(fire));
                slot = null;
                return false;
            }
            if (fireParticles.TryGetValue(fire.Idx, out slot)) {
                D.Err("Fire particle already exists for fire {0} -> {1}",
                    D.DumpInstance(fire), D.DumpInstance(slot));
                return true;
            }
            InitializeParticles();
            if (!fireRend.TryReserveSlot(out slot)) {
                #if DEBUG_FIREX
                D.Err("Failed to reserve slot for a new fire {0}",
                    D.DumpInstance(fire));
                #endif
                // Once per session, so invisible fire reports (STA-1623) show whether
                // the fire had no particle at all
                if (!warnedNoFireSlot) {
                    warnedNoFireSlot = true;
                    D.Warn("No particle slot for a fire, it will be invisible: {0}. Particle budget: {1}/{2}",
                        fire, ParticleBudget.ActiveCount, ParticleBudget.MaxBudget);
                }
                return false;
            }
            AddFireAmbience(fire);
            fireParticles.Add(fire.Idx, slot);
            var slotPos = new float2(
                fire.Position.x, fire.Position.y);
            slot.Pos = slotPos;
            slot.IsVisible = true;
            UpdateFireLight(in fire);
            arrFires.Array[fire.Idx] = fire;
            return true;
        }

        private static bool warnedNoFireSlot;

        // Every burning fire has a visible particle of its own, drawn where it burns
        // (invisible fire, STA-1623)
        internal void CollectFireVisualProblems(List<string> problems) {
            var seen = new HashSet<int>();
            for (int i = 0; i < arrFires.Count; i++) {
                var fire = arrFires.Array[i];
                if (!fire.IsValid) { continue; }
                if (!fireParticles.TryGetValue(fire.Idx, out var slot)) {
                    problems.Add($"fire {fire.Idx} has no particle slot");
                    continue;
                }
                if (!seen.Add(slot.Idx)) {
                    problems.Add($"fire {fire.Idx} shares particle slot {slot.Idx}");
                }
                if (slot.Visibility < 1f) {
                    problems.Add($"fire {fire.Idx} particle slot {slot.Idx} is hidden");
                }
                if (fireRend == null || !fireRend.IsLive(slot)) {
                    problems.Add($"fire {fire.Idx} particle slot {slot.Idx} is not drawn");
                } else {
                    var drawn = fireRend.DrawnPos(slot);
                    if (Vector2.Distance(drawn, fire.Position) > 0.01f) {
                        problems.Add($"fire {fire.Idx} drawn at {drawn} but burns at {fire.Position}");
                    }
                }
                if (Vector2.Distance(new Vector2(slot.Pos.x, slot.Pos.y), fire.Position) > 0.01f) {
                    problems.Add($"fire {fire.Idx} particle at {slot.Pos} but burns at {fire.Position}");
                }
            }
        }

        private void UpdateFireLight(in Fire fire) {
            if (!fireLights.TryGetValue(fire.Idx, out var lightIdx)) {
                if (!S.Sys.Light.TryReserveSlot(out var newLightIdx)) {
                    #if !PRODUCTION_BUILD
                    D.Warn("Failed reserving light slot for fire: #{0}", fire.Idx);
                    #endif
                    return;
                }
                fireLights.Add(fire.Idx, newLightIdx);
                lightIdx = newLightIdx;
            }
            var lightSource = new LightSource();
            lightSource.Position = fire.Position;
            lightSource.Color = Consts.ColorFireFromF3;
            lightSource.Color2 = Consts.ColorFireToF3;
            lightSource.PulseFreq = 0.02f + Rng2.ZeroToOne.Noise1D(
                lightIdx * 8762341, 5436781) * 0.05f;
            lightSource.Range = fire.StrengthWithSize * 4f;
            lightSource.Type = 1;
            S.Sys.Light.SetLight(lightIdx, lightSource);
        }

        protected override void OnInitialize() {
            // Must exist before ship generation, which can set things on fire
            Boost = new FireBoost(S);
            Prepare();
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        private void UpdateTunables() {
            var heatupCurve = Tunables.FireHeatupCurveCelsius;
            // Kelvinize
            for (int i = 0; i < heatupCurve.Length; i += 2) {
                heatupCurve[i] = Units.CelsiusToKelvin(heatupCurve[i]);
            }
            fireHeatupCurveKelvin = heatupCurve;
            FireChokeRate = Tunables.FireChokeRate;
            FireStrengthAfterSpread = Tunables.FireStrengthAfterSpread;
            FireStrengthIncreaseRate = Tunables.FireStrengthIncreaseRate;
            FireNewFlameStrengthAfterSpread = Tunables.FireNewFlameStrengthAfterSpread;
            FireFuelConsumptionRate = Tunables.FireFuelConsumptionRate;
            FireO2ConsumptionRate = Tunables.FireO2ConsumptionRate;
            FireSpreadRate = Tunables.FireSpreadRate;
            FireMaxSpreadPerTick = Tunables.FireMaxSpreadPerTick;
            ColdChanceDamage = Tunables.ColdChanceDamage;
            HeatChanceDamage = Tunables.HeatChanceDamage;
            ColdDamagePer100Deg = Tunables.ColdDamagePer100Degrees;
            HeatDamagePer100Deg = Tunables.HeatDamagePer100Degrees;
            if (FireMaxSpreadPerTick < 1) {
                D.Err("FireMaxSpreadPerTick cannot be < 1, fixing");
                FireMaxSpreadPerTick = 1;
            }
            FireSpreadToWallChance = Tunables.FireSpreadToWallChance;
            FireStrengthAfterFailedSpread = Tunables.FireStrengthAfterFailedSpread;
            // we process flammables every 10 ticks
            FireMinDamagePerFlammableProcessCycle =
                Tunables.FireMinDamagePerMinute / Consts.TicksPerMinute * 10;
            FireMaxDamagePerFlammableProcessCycle =
                Tunables.FireMaxDamagePerMinute / Consts.TicksPerMinute * 10;
        }
        private void Prepare() {
            #if DISABLE_FIREX
            return;
            #endif
            UpdateTunables();
            arrFlams = new ReusableNativeArray<Flammable>(
                "Flammables", 1024, 1024 * 1024);
            arrFires = new ReusableNativeArray<Fire>("Fire", 64, 1024 * 64);
        }


        private void OnLoadSave(GameState state) {
            #if DISABLE_FIREX
            return;
            #endif
            Firefight = new Firefighting(this);
            Explosions = new Explosions(this);
            state.Clock.OnTick.AddListener(OnTick);
            state.Clock.OnUpdate60.AddListener(EveryFrame);
            state.Sig.ShipMoved.AddListener(OnShipMoved);
            RestoreData();
        }

        private void EveryFrame() {
            #if DISABLE_FIREX
            return;
            #endif
            for (int i = 0; i < arrFires.Count; i++) {
                var fire = arrFires.Array[i];
                // WE DONT WANT TO UPDATE THIS FIRE
                if (!fire.IsValid || !fire.CanMove) { continue; }
                SyncFireVisuals(i);
            }
            CheckFireForceReinit();
        }

        // Winching/rotation relocate tiles directly on the grid without advancing the
        // clock (it's force-stopped for the whole operation), so a burning tile's Fire
        // struct - normally refreshed from the tick-driven AfterProcessFire - would
        // otherwise leave its particle/light/ambience behind until the move finishes.
        // Re-sync every active fire's visuals (not just the CanMove ones EveryFrame
        // handles) on every step so fire visibly tracks its tile through the move.
        private void OnShipMoved(int dx, int dy) {
            #if DISABLE_FIREX
            return;
            #endif
            for (int i = 0; i < arrFires.Count; i++) {
                if (arrFires.Array[i].IsValid) {
                    SyncFireVisuals(i);
                }
            }
        }

        private void SyncFireVisuals(int idx) {
            var fire = arrFires.Array[idx];
            if (!flamComps.TryGetValue(fire.FlamIdx, out var flam)) { return; }
            var pos = flam.Entity.SelectPosition;
            fire.Position = pos;
            fire.PosIdx = flam.Entity.PosIdx;
            if (fireParticles.TryGetValue(fire.Idx, out var slot)) {
                slot.Pos = pos;
            }
            if (fireSounds.TryGetValue(fire.Idx, out var amb)) {
                amb.Position = pos;
            }
            UpdateFireLight(in fire);
        }

        private void CheckFireForceReinit() {
            if (fireRend == null || arrFires.Count == 0) { return; }
            var now = Time.realtimeSinceStartupAsDouble;
            CheckDeadFireShards(now);
            if (now - lastFireForceReinitTime < fireForceReinitIntervalSeconds) { return; }
            lastFireForceReinitTime = now;
            fireRend.ForceReinit();
        }

        // Invisible fire (STA-1623, FireSystem.md): fires that burn and glow but draw no
        // flames, only in sessions hours long. A shard with burning slots and no live
        // particles is that state; reinit it, and log it once so reports carry the evidence
        private void CheckDeadFireShards(double now) {
            // Long enough after any reinit for the GPU particle count to have caught up
            if (now - lastDeadFireShardCheckTime < deadFireShardCheckIntervalSeconds) { return; }
            if (now - lastFireForceReinitTime < deadFireShardCheckIntervalSeconds) { return; }
            lastDeadFireShardCheckTime = now;
            // A shard moved or scaled away from identity draws live flames away from the
            // fires (a pooled effect that kept a space object's scale, STA-1854). Logged when
            // it happens, since players often restart before they report
            var detailsSb = warnedMovedFireShard ? null : new System.Text.StringBuilder();
            var moved = fireRend.RepairShardTransforms(detailsSb);
            if (moved > 0 && !warnedMovedFireShard) {
                warnedMovedFireShard = true;
                D.Warn("Fire shards drawn away from their fires: {0},{1} reset. Session: {2:0} min",
                    moved, detailsSb, now / 60);
            }
            var found = fireRend.ReinitShardsWithoutParticles(0.1f, out var visibleSlots);
            if (found > 0 && !warnedDeadFireShard) {
                warnedDeadFireShard = true;
                D.Warn("Fire shards with burning slots but no particles: {0} ({1} slots), reinit. Session: {2:0} min",
                    found, visibleSlots, now / 60);
            }
        }

        private const double deadFireShardCheckIntervalSeconds = 60;
        private double lastDeadFireShardCheckTime;
        private static bool warnedDeadFireShard;
        private static bool warnedMovedFireShard;

        // One line for bug reports: what burns, what the checks find, and each fire shard's
        // state, so an invisible fire report shows which part failed without the logs
        internal string VisualsLine() {
            if (fireRend == null) {
                return $"{OnFireCount} burning, no fire renderer yet";
            }
            var problems = new List<string>();
            CollectFireVisualProblems(problems);
            var sb = new System.Text.StringBuilder();
            sb.Append($"{OnFireCount} burning, {problems.Count} problems");
            if (problems.Count > 0) {
                sb.Append($" (first: {problems[0]})");
            }
            sb.Append(". Shards:");
            fireRend.DescribeShards(sb);
            return sb.ToString();
        }

        // canBoost false starts the fire without FireBoost's head start, for scripted fires
        // that are meant to be easy to put out
        public bool TrySetOnFire(Entity entity, in Fire fire, bool canBoost = true) {
            #if DISABLE_FIREX
            return false;
            #endif
            if (entity == null) { return false; }
            if (!entity.IsActive) { return false; }
            #if !PRODUCTION_BUILD
            if (!GameState.IsCurrent(entity.S)) {
                throw new Exception("Trying to set on fire an entity that is not of current state!");
            }
            #endif
            Tile tile = null;
            if (entity is Tile t) {
                if (t.Transform.IsChild) {
                    entity = tile = t.Transform.Parent;
                } else {
                    tile = t;
                }
                if (!tile.IsConstructed) { return false; }
            }
            var flam = entity.Flammable;
            if (flam == null) {
                D.Err("Entity has no FlammableComp! {0}", entity);
                return false;
            }
            return TrySetOnFire(flam, fire, canBoost);
        }
        public bool TrySetOnFire(FlammableComp flam, Fire fire, bool canBoost = true) {
            if (flam.Being != null) {
                D.Warn("Trying to set on fire: {0} {1}", flam.Being, fire);
            }
            #if DISABLE_FIREX
            return false;
            #endif
            if (flam == null) { return false; }
            if (flam.Tile?.IsConstructed == false) { return false; }
            if (flam.IsTemperatureResistant) { return false; }
            if (flam.IsOnFire) {
                #if !PRODUCTION_BUILD
                D.Err("Trying to set on fire what is already on fire! {0}", D.DumpInstance(flam));
                #else
                D.Err("Trying to set on fire what is already on fire! {0}", flam.Entity);
                #endif
                return false;
            }
            if (!flam.IsActive) {
                if (!Ready.Gen) {
                    // destroy ship gen phase does that
                    AddFlammable(flam);
                } else {
                    D.Err("Trying to set on fire an unregistered FlammableComp: {0}",
                        flam.Entity);
                    #if !PRODUCTION_BUILD
                    throw new Exception("Trying to set on fire an unregistered FlammableComp!");
                    #endif
                }
            }
            var entity = flam.Entity;
            if (!entity.IsActive) { return false; }
            #if !PRODUCTION_BUILD
            var tile = flam.Tile;
            if (tile != null && tile.Transform.IsChild) {
                throw new Exception("TrySetOnFire invoked on child flammable comp!");
            }
            #endif
            fire.CanMove = flam.Tile == null;
            fire.PosIdx = entity.PosIdx;
            fire.Position = entity.SelectPosition;
            fire.ResizeFor(entity.Size);
            return SetOnFire(flam, ref fire, canBoost);
        }

        private bool RemoveFire(in Fire fire) {
            // #if !PRODUCTION_BUILD
            // if (fire.Ticks < 2 && Ready.Gen) {
            //     // nothing to worry about, explosion can do that, gen can do that, etc
            //     D.ErrSoft("Removing fire that has less than 2 ticks!");
            // }
            // #endif
            #if DISABLE_FIREX
            return true;
            #endif
            // D.Warn("Removing fire: {0}", D.DumpInstance(fire));
            if (!fire.IsValid) {
                D.Err("Trying to remove invalid fire! {0}", D.DumpInstance(fire));
                return false;
            }
            S.Sys.Dirt.Add(DirtData.Create(fire.PosIdx, Images.RandomMatching(
                GraphicsId.DirtFireMark01, 2), Rng.URange(1f, 2f)));
            if (fireLights.TryGetValue(fire.Idx, out var lightIdx)) {
                fireLights.Remove(fire.Idx);
                S.Sys.Light.RemoveLight(lightIdx);
            } else {
                D.Warn("Failed removing fire light: {0}", D.DumpInstance(fire));
            }
            if (fireParticles.TryGetValue(fire.Idx, out var slot)) {
                fireParticles.Remove(fire.Idx);
                fireRend.ReleaseSlot(slot);
            } else {
                #if DEBUG_FIREX
                D.Err("Failed removing particle for fire: {0}", D.DumpInstance(fire));
                #endif
            }
            RemoveFireAmbience(fire);
            if (!arrFires.Remove(fire.Idx)) {
            // if (!arrFires.RemoveWithDebug(fire, fire.Idx, fireComparer)) {
                D.Err("Failed to remove fire: {0}", D.DumpInstance(fire));
                return false;
            }
            S.Sig.FireChanged.Send(fire, false);
            return true;
        }

        private bool LoadFire(in Fire fire) {
            if (!fire.IsValid) {
                D.Err("Trying to load invalid fire: {0}", D.DumpInstance(fire));
                return false;
            }
            if (Pos.IsOOB(fire.PosIdx)) {
                D.Err("Trying to load a fire that is in invalid pos: {0}",
                    Pos.String(fire.PosIdx));
                return false;
            }
            if (AddFireAudioVisuals(fire, out var slot)) {
                slot.Offset.x = fire.StrengthWithSize;
                slot.Offset.y = fire.FireFuelAmt;
            } else {
                #if DEBUG_FIREX
                D.Err("Failed adding fire particle for {0}",
                    D.DumpInstance(fire));
                #endif
            }
            return true;

        }

        private bool AddFire(ref Fire fire, ref Flammable flam) {
            #if DISABLE_FIREX
            return false;
            #endif
            fire.EntityId = flam.EntityId;
            if (!fire.IsValid) {
                D.Err("Trying to add a fire for a flammable with entity id 0! {0}. Flam: {1}",
                    D.DumpInstance(fire), D.DumpInstance(flam));
                return false;
            }
            if (fire.Strength < 0.001f) {
                D.Err("Trying to add fire that is too weak!: {0}", D.DumpInstance(fire));
                return false;
            }
            if (Pos.IsOOB(fire.PosIdx)) {
                D.Err("Trying to add a fire that is in invalid pos: {0}",
                    Pos.String(fire.PosIdx));
                return false;
            }
            #if !PRODUCTION_BUILD
            if (fire.PosIdx == 0) {
                throw new Exception($"AddFire at pos 0: {D.DumpInstance(fire)}");
            }
            #endif
            fire.IsWall = flam.IsWall;
            if (arrFires.TryReserveSlot(out var idx)) {
                fire.Idx = idx;
                fire.Ticks = 1;
                if (AddFireAudioVisuals(fire, out var slot)) {
                    slot.Offset.x = fire.StrengthWithSize;
                    slot.Offset.y = fire.FireFuelAmt;
                } else {
                    #if DEBUG_FIREX
                    D.Err("Failed adding fire particle for {0}",
                        D.DumpInstance(fire));
                    #endif
                }
                flam.FireIdx = idx;
                fire.FlamIdx = flam.Idx;
                flam.TicksOnFire++;
                arrFires.Update(idx, fire);
                arrFlams.Update(flam.Idx, flam);
                S.Sig.FireChanged.Send(fire, true);
                // S.Situation.AddTension(0.25f, "Fire");
                return true;
            } else {
                D.Err("Failed to reserve a slot for fire: {0}", D.DumpInstance(fire));
                return false;
            }
        }

        private bool SetOnFire(FlammableComp flamComp, ref Fire fire, bool canBoost) {
            #if DISABLE_FIREX
            return false;
            #endif
            #if DEBUG_FIREX
            D.Warn("Setting on fire: {0}", flamComp);
            #endif
            if (arrFlams.TryGet(flamComp.SlotIdx, out var flam)) {
                if (!flam.IsValid) {
                    D.Err("Trying to set on fire but flam is invalid! {0} -> {1}",
                        flamComp.Entity, D.DumpInstance(flamComp));
                    return false;
                }
                if (flam.IsOnFire) {
                    // D.Err("Trying to set on fire what is already on fire: {0} -> fire we want: {1}",
                    //     flamComp.Entity, D.DumpInstance(fire));
                    return false;
                }
                // before the cycle stamp, so a refused ignition leaves no stamp
                if (IsFoamProtected(flamComp)) {
                    return false;
                }
                if (flam.FireCycle == FireCycle) {
                    return false;
                }
                flam.FireCycle = FireCycle;
                // 1. get the fresh up to date Flammable for flamComp
                if (AddFire(ref fire, ref flam)) {
                    // A fire that starts an incident gets a head start, so it won't
                    // get extinguished before it has a chance to become a threat
                    if (canBoost && ExtinguishableFireCount == 0
                            && IsWorthExtinguishing(flamComp)) {
                        Boost.TryStart();
                    }
                    DealFireDamageTo(flamComp);
                    flamComp.Update(flam);
                    return true;
                }
            }
            return false;
        }

        // True if this flammable can catch fire right now: not already burning, and
        // not already burned during the current fire cycle. Story events should use
        // this instead of just checking IsOnFire when picking an ignition target, or
        // they will frequently pick something that just burned and got extinguished
        public bool CanIgnite(FlammableComp flamComp) {
            if (flamComp == null || flamComp.IsOnFire) { return false; }
            if (IsFoamProtected(flamComp)) { return false; }
            if (!arrFlams.TryGet(flamComp.SlotIdx, out var flam)) { return false; }
            return flam.IsValid && flam.FireCycle != FireCycle;
        }

        // Firefoam keeps fire off everything on a foamed tile but the living: they are only
        // protected for a while after a spray covered them (FirefoamBurst). Checked where
        // every ignition passes (SetOnFire), so spreading into foam just fails.
        public bool IsFoamProtected(FlammableComp flamComp) {
            var being = flamComp.Being;
            if (being != null && being.IsAlive) {
                return flamComp.FoamCoverUntil > G.Ticks;
            }
            return S.Sys.Dirt.HasFoam(flamComp.Entity.PosIdx);
        }

        private bool DealFireDamageTo(FlammableComp comp) {
            if (comp.Entity.IsActive) {
                return comp.TakeDamage(
                    Rng.URange(FireMinDamagePerFlammableProcessCycle,
                        FireMaxDamagePerFlammableProcessCycle), DamageType.Heat);
            }
            return false;
        }

        private void RestoreData() {
            if (data == null) {
                data = new FirexSysData();
                Ready.Set(WhenH.FirexLoaded, true);
                return;
            }
            Boost.UntilTicks = data.FireBoostUntilTicks;
            // Legacy saves store 0; without the bump, stamp 0 == cycle 0 would
            // block the first ignition after loading
            FireCycle = data.FireCycle > 0 ? data.FireCycle : 1;
            if (data.Flams == null) {
                // We didn't have any data, we must migrate!
                // var allFlams = S.Components.AllByType<FlammableComp>();
                MigrateLegacyFlammables();
                Ready.Set(WhenH.FirexLoaded, true);
                return;
            }

            arrFlams.LoadData(data.FlamsCount, data.FlamsRecycler, data.Flams);
            data.Flams = null;
            data.FlamsRecycler = null;
            data.FlamsCount = 0;

            if (data.Fires != null) {
                arrFires.LoadData(data.FiresCount, data.FiresRecycler, data.Fires);
                data.Fires = null;
                data.FiresRecycler = null;
                data.FiresCount = 0;
            }
            // OnFireCount = 0;
            // for (int i = 0; i < arrFires.Count; i++) {
            //     if (arrFires.TryGet(i, out var fire)) {
            //         if (fire.IsValid) {
            //             OnFireCount++;
            //             LoadFire(fire);
            //         }
            //     }
            // }

            var loadedIds = Caches.IntSet;
            loadedIds.Clear();
            for (int i = 0; i < arrFlams.Count; i++) {
                if (arrFlams.TryGet(i, out var flam)) {
                    if (!flam.IsValid) { continue; }
                    var comp = S.Components.Find<FlammableComp>(flam.EntityId);
                    if (comp == null) {
                        if (S.Map.TryEntityById(flam.EntityId, out var ntt)) {
                            D.Err("Trying to load a flammable comp is null by id! {0} idx:{1}. Entity exists: {2}",
                                flam.EntityId, flam.Idx, ntt);
                        } else {
                            D.Log("Trying to load a flammable comp is null by id! {0} idx:{1}. Entity no longer exists",
                                flam.EntityId, flam.Idx);
                        }
                        arrFlams.Remove(i);
                        continue;
                    }
                    if (comp.Entity is Tile t && !t.IsConstructed) {
                        D.Err("Trying to load a flammable where comp is not constructed! {0} {1}",
                            flam, comp);
                        arrFlams.Remove(i);
                        continue;
                    }
                    if (flam.Idx != i) {
                        D.Err("Trying to load a flammable where idx does not match number! idx:{0} != idx:{1}. {2}",
                            flam.Idx, i, comp);
                        arrFlams.Remove(i);
                        continue;
                    }
                    if (!loadedIds.Add(flam.EntityId)) {
                        var existing = comp.Flam;
                        D.Err("Duplicate flammable for entity with id: {0} detected! {1}. Copy: {2}. Existing: {3}. Fixing",
                            flam.EntityId, comp, flam, existing);
                        arrFlams.Remove(i);
                        continue;
                    }
                    if (comp != null) {
                        if (flam.IsOnFire) {
                            if (!arrFires.TryGet(flam.FireIdx, out var fire)) {
                                D.Err("flam is on fire but fire not found by idx: {0} | {1}", flam.FireIdx, flam);
                                flam.FireIdx = 0;
                                flam.TicksOnFire = 0;
                            }
                            if (!fire.IsValid) {
                                D.ErrSoft("flam is on fire but fire no longer valid: {0} | {1}", flam, fire);
                                flam.FireIdx = 0;
                                flam.TicksOnFire = 0;
                            }
                        }
                        flam.NeedsSync = true;
                        var loaded = comp.LoadFlammable(flam);
                        var idx = loaded.Idx;
                        if (!flamComps.TryAdd(loaded.Idx, comp)) {
                            D.Err("Trying to load flammable component by idx {0} twice! {1} -> {2}. Existing: {3}",
                                idx, comp, loaded, flamComps[idx]);
                        } else {
                            arrFlams.Update(idx, loaded);
                        }
                    } else {
                        D.Err("Failed restoring flammable - not found by entity id: {0}",
                            flam.EntityId);
                        arrFlams.Remove(i);
                    }
                }
            }

            // Final check to see if everything is registered
            var allFlams2 = S.Components.AllByType<FlammableComp>();
            if (allFlams2 != null) {
                foreach (FlammableComp flam in allFlams2) {
                    if (!flam.IsActive) {
                        if (flam.Tile != null && !flam.Tile.IsConstructed) {
                            // the only legitimate situation
                            continue;
                        }
                        if (flam.Being != null) {
                            D.Err("Flammable being was unregistered after load! Fixing for: {0}. F: {1}.",
                                flam.Entity, flam);
                        } else {
                            D.Err("Flammable was unregistered after load! Fixing for: {0}. H: {1}",
                                flam.Entity, flam.Health);
                        }
                        AddFlammable(flam);
                    }
                }
            }

            OnFireCount = 0;
            ExtinguishableFireCount = 0;
            for (int i = 0; i < arrFires.Count; i++) {
                if (arrFires.TryGet(i, out var fire)) {
                    if (fire.IsValid) {
                        OnFireCount++;
                        if (IsWorthExtinguishing(fire.FlamIdx)) {
                            ExtinguishableFireCount++;
                        }
                        LoadFire(fire);
                    }
                }
            }
            Ready.Set(WhenH.FirexLoaded, true);
        }

        public bool IsWorthExtinguishing(int flamIdx) {
            if (flamComps.TryGetValue(flamIdx, out var flam)) {
                return IsWorthExtinguishing(flam);
            }
            return false;
        }
        public bool IsWorthExtinguishing(FlammableComp flam) {
            if (flam == null) {
                return false;
            }
            // is the fire in space?
            var being = flam.Being;
            if (being?.Data != null && being.IsAlive) {
                if (being.Data.IsFriendly) { return true; }
                // A downed hostile can't resist; left burning it re-ignites whatever gets
                // extinguished around it (forever, for robots) and blocks storing it
                if (being.CanFight) { return false; }
            }
            if (EntityUtils.IsWithinMargin(flam.Entity.PosIdx, false, false)) {
                return !EntityUtils.IsEmptySpaceAt(flam.Entity.PosIdx);
            }
            return false;
        }

        // Effective spread rate, taking the fresh fire boost into account
        public float SpreadRateFor(FlammableComp flam) {
            return FireSpreadRate * Boost.SpreadMulFor(flam);
        }

        public float SpreadRateFor(int flamIdx) {
            flamComps.TryGetValue(flamIdx, out var flam);
            return SpreadRateFor(flam);
        }

        private void OnTick(long ticks) {
            if (!Ready.Gen) { return; }
            Explosions.Tick(ticks);
            if (arrFires.Count > 0) {
                ProcessFire(ticks);
            }
            var advanceFireCycle = false;
            if (ExtinguishableFireCount == 0) {
                if (hadExtinguishableFireLastTick) {
                    advanceFireCycle = true;
                }
                hadExtinguishableFireLastTick = false;
            } else {
                hadExtinguishableFireLastTick = true;
            }
            // A fire that is never worth extinguishing (space, hostile beings) still
            // stamps everything it touches but can't trigger the block above, so it
            // would otherwise hold those stamps hostage until an unrelated
            // extinguishable incident starts and ends
            if (OnFireCount == 0) {
                if (hadAnyFireLastTick) {
                    advanceFireCycle = true;
                }
                hadAnyFireLastTick = false;
            } else {
                hadAnyFireLastTick = true;
            }
            if (advanceFireCycle) {
                FireCycle++;
            }
            if (ticks % 10 == 2) {
                ProcessFlammables(ticks);
            }
            if (OnFireCount > 0) {
                if (!IsAnythingOnFire) {
                    S.Situation.TimesOnFire++;
                    IsAnythingOnFire = true;
                }
                if (ExtinguishableFireCount > 0) {
                    Firefight.AfterProcessFire(ticks);
                }
            } else {
                IsAnythingOnFire = false;
            }
        }

        public void ProcessFire(long ticks) {
            #if DISABLE_FIREX
            return;
            #endif
            var fireJob = new SimulateFireJob {
                Ticks = ticks,
                Fires = arrFires.Array,
                FiresCount = arrFires.Count,
                FireChokeRate = FireChokeRate,
                FireStrIncreaseRate = FireStrengthIncreaseRate * Boost.GrowthMul,
                SpreadStrThreshold = Boost.SpreadStrThreshold,
                FireFuelConsumptionRate = FireFuelConsumptionRate,
                FireO2ConsumptionRate = FireO2ConsumptionRate,
                GridH = S.GridHeight,
                InO2 = S.Map.O2.Data,
                InTemp = S.Map.Temp.Data,
            };
            using (D.Timer("FireJob", 0.005f)) {
                fireJob.Run();
            }
            AfterProcessFire();
        }

        public void ProcessFlammables(long ticks, bool skipCallbacks = false) {
            #if DISABLE_FIREX
            return;
            #endif
            #if DEBUG_FIREX
            var startTime = Time.realtimeSinceStartupAsDouble;
            D.Warn("Ticking flammables: {0}", arrFlams.Count);
            #endif
            var outChanged = new NativeList<int>(Allocator.TempJob);
            var flamsJob = new ProcessFlammablesJob {
                Ticks = ticks,
                Flams = arrFlams.Array,
                FlamsCount = arrFlams.Count,
                InO2 = S.Map.O2.Data,
                InTemp = S.Map.Temp.Data,
                OutChanged = outChanged,
            };
            using (D.Timer("TickFlamsJob", 0.005f)) {
                flamsJob.Run();
            }
            var endJobTime = Time.realtimeSinceStartupAsDouble;
            AfterProcessFlammables(ticks, outChanged);
            #if DEBUG_FIREX
            var endTime = Time.realtimeSinceStartupAsDouble;
            D.Warn("Done Ticking flammables: {0} in {1}s. Job: {2}s. Callbacks: {3}s",
                arrFlams.Count,
                    endTime - startTime,
                    endJobTime - startTime,
                    endTime - endJobTime);
            #endif
        }

        private void AfterProcessFire() {
            #if DISABLE_FIREX
            return;
            #endif
            int fireSpreadThisTick = 0;
            OnFireCount = 0;
            ExtinguishableFireCount = 0;
            using (D.Timer("UpdateFire", 0.01f)) {
                for (int i = arrFires.Count - 1; i >= 0; i--) {
                    #if FIREX_SAFE
                    var fire = arrFires.Array[i];
                    #else
                    ref var fire = ref arrFires.Array.GetUnsafeRef(i);
                    #endif
                    if (!fire.IsValid) { continue; }
                    if (fire.Strength < 0.1f) {
                        var fidx = fire.FlamIdx;
                        if (flamComps.TryGetValue(fire.FlamIdx, out var extFlam)) {
                            if (extFlam.EntityId != fire.EntityId) {
                                #if !PRODUCTION_BUILD
                                // This is a race condition:
                                // Flammable was destroyed and the flammable slot was repurposed
                                // by some other entity, but fire flam idx wasn't updated properly (TODO: find out why)
                                // We will check if the EntityId that belongs to fire is gone, and if it is, we just
                                // silently kill the fire, if not, it's a serious issue
                                if (!S.Map.TryEntityById(fire.EntityId, out var ntt)
                                        || !ntt.IsActive) {
                                    // this was correct, entity inactive
                                    RemoveFire(fire);
                                    continue;
                                } else {
                                    // ntt is not null here, amd ID does not match
                                    D.Err("Fire Entity ID does not match with linked flammable comp Entity ID! {0} != {1}", fire.EntityId, extFlam.EntityId);
                                    D.Err("Actual Entity: {0}. Flam: {1}", extFlam.Entity, D.DumpInstance(extFlam));
                                    D.Err("Expected Entity: {0}. Flam: {1}", ntt, D.DumpInstance(ntt.Flammable));
                                    D.Err("Fire: {0}", D.DumpInstance(fire));
                                    // let's not have eternal flames
                                    RemoveFire(fire);
                                    continue;
                                }
                                #else
                                // For performance reasons do not do this witch hunt in production
                                RemoveFire(fire);
                                continue;
                                #endif
                            }
                            if (!extFlam.IsOnFire) {
                                // lets check if maybe this fire is somehow already removed
                                Fire actualFire;
                                if (arrFires.TryGet(fire.Idx, out actualFire)) {
                                    if (!actualFire.IsValid) {
                                        // the fire is removed, let's just move on, fuck it
                                        continue;
                                    }
                                } else {
                                    // the fire did not exist, whatever
                                    D.Err("Unexpected: Trying to remove fire but it did not exist!: {0}",
                                        D.DumpInstance(fire));
                                    continue;
                                }

                                /*
                                #if !PRODUCTION_BUILD
                                if (!The.TestsAreRunning) {
                                #endif
                                D.Err("Trying to remove a fire that is valid but extFlam is not on fire! ExtFlam: {0}. Fire flam idx = {1}. Flam idx = {2}. IsReg: {3}. Entity: {4}. Health: {5}",
                                    extFlam, fire.FlamIdx, extFlam.Flam.Idx, extFlam.IsActive, extFlam.Entity,
                                        extFlam.Entity.GetComponent<DamageableComp>());
                                D.Err("Fire we are trying to extinguish: {0}", D.DumpInstance(fire));
                                D.Err("Fire actual: {0}", D.DumpInstance(actualFire));
                                D.Err("Flammable: {0}", D.DumpInstance(extFlam.Flam));
                                #if !PRODUCTION_BUILD
                                }
                                #endif
                                */
                                RemoveFire(fire);
                                continue;
                            } else if (TryExtinguish(extFlam.Entity)) {
                                continue;
                            } else {
                                D.Err("Removing fire for an unregistered flammable: {0}. Fire: {1}",
                                    extFlam, D.DumpInstance(fire));
                                RemoveFire(fire);
                                continue;
                            }
                        }
                        if (extFlam != null) {
                            D.Err("Failed to extinguish fire flammable: {0} | Entity: {1}",
                                extFlam.Entity,
                                D.DumpInstance(fire, forLog: true));
                        }
                        RemoveFire(fire);
                        continue;
                    }
                    // we still want to sync the fire position for those that move
                    // because in everyframe we do not update the fire struct
                    if (flamComps.TryGetValue(fire.FlamIdx, out var flam)) {
                        fire.PosIdx = flam.Entity.PosIdx;
                        fire.Position = flam.Entity.SelectPosition;
                    }
                    if (fireParticles.TryGetValue(fire.Idx, out var slot)) {
                        slot.Pos = fire.Position;
                        slot.Offset.x = fire.StrengthWithSize;
                        slot.Offset.y = fire.FireFuelAmt;
                    }
                    // don't consider weak flames as on fire
                    var isFireWeak = fire.Strength < 0.05f
                            && fire.FireFuelAmt < 0.001f && fire.O2 < 0.001f;
                    if (!isFireWeak) {
                        OnFireCount++;
                        if (IsWorthExtinguishing(flam)) {
                            ExtinguishableFireCount++;
                        }
                    }
                    S.Map.O2.Set(fire.PosIdx, fire.O2);
                    var newTemp = fire.Temp + fireHeatupCurveKelvin.Evaluate(fire.Temp);
                    S.Map.Temp.Set(fire.PosIdx, newTemp);
                    UpdateFireLight(in fire);
                    if (fire.SpreadStr > 0f && fireSpreadThisTick < FireMaxSpreadPerTick) {
                        if (Rng.UChance(fire.SpreadStr * SpreadRateFor(flam))) {
                            // If the fire is in the corner
                            // it may or may not spread to wall
                            // if it did, we will return a true here
                            // but if it didn't spread, we still want to set
                            // SpreadStr to 0 so it won't try again too fast
                            if (TrySpreadFire(in fire, out bool removeNow)) {
                                fireSpreadThisTick++;
                                fire.Strength = FireStrengthAfterSpread;
                            } else {
                                // if fire failed to spread, we will
                                // reduce the str a bit so it won't try to spread
                                // immediately again
                                fire.Strength = 0.9f;
                            }
                            fire.SpreadStr = 0f;
                            if (removeNow) {
                                RemoveFire(in fire);
                                fire.Strength = 0;
                                fire.EntityId = 0;
                            }
                        }
                    }
                    arrFires.Array[i] = fire;
                }
            }
        }
        //private readonly Dictionary<FlammableComp, float> fireSpreadWeights = new(8);
        private bool TrySpreadFire(in Fire fire, out bool removeNow) {
            removeNow = false;
            #if DISABLE_FIREX
            return false;
            #endif
            if (S.Prefs.GetBool(Pref.NoFireSpread, PrefH.NoFireSpread, false)) {
                return false;
            }
            int step = 0;
            try {
                FlammableComp target = null;
                float targetWeight = float.MinValue;
                // 1. check if there is any surroundings that are not burning
                //fireSpreadWeights.Clear();
                step = 1;
                if (!flamComps.TryGetValue(fire.FlamIdx, out var flamComp)) {
                    D.Err("Fire does not have flammable by flam idx: {0}",
                        fire.FlamIdx, D.DumpInstance(fire));
                    removeNow = true;
                }
                if (flamComp == null) {
                    D.Err("null flam component was in the flamComps!");
                    removeNow = true;
                    return false;
                }
                step = 2;
                int count = 0;
                int[] positions;
                if (flamComp.Tile != null && flamComp.Tile.Transform?.IsMulti == true) {
                    step = 3;
                    var outline = flamComp.Tile.Transform.Outline;
                    count = outline.Length;
                    positions = outline;
                } else {
                    step = 4;
                    count = Pos.Around(fire.PosIdx, Caches.PosIdx9, false);
                    positions = Caches.PosIdx9;
                }
                // does this fire belong to a multi tile obj?
                int wallCount = 0;
                int nonWallCount = 0;
                for (int i = 0; i < count; i++) {
                    step = 5;
                    var pos = positions[i];
                    // Tile
                    var tileAt = EntityUtils.GetTopmostParentTile(pos);
                    if (tileAt == null) {
                        // this is empty space, let's not spread into it
                        continue;
                    }
                    if (!tileAt.IsActive) { continue; }
                    step = 6;
                    // since temperature is 0 - ~1000 and oxygen is 0 - 0.21
                    // we want to normalize the oxygen, so 0.21 would be around 600
                    // so we can have a fair weight when compare it to temp
                    var oxygenWeight = S.Map.O2.At(pos) * 3000f;
                    var temp = S.Map.Temp.At(pos);
                    // res can be from 0 to 1. High resistance should reduce
                    var tempResWeight = S.Map.Temp.ResAt(pos) * 500f;
                    var posWeight = oxygenWeight + temp - tempResWeight;
                    if (posWeight < 1f) {
                        // 99% its a wall
                        posWeight = 1f;
                    }

                    step = 7;
                    if (S.Objs.ByPos.TryGetValue(pos, out var objs)) {
                        for (int i1 = objs.Count - 1; i1 >= 0; i1--) {
                            step = 8;
                            Obj o = objs[i1];
                            if (!o.IsActive || o.Flammable.IsOnFire) { continue; }
                            step = 9;
                            var wo = o.Flammable.AdjustFireSpreadWeight(posWeight);
                            if (wo > 0f) {
                                nonWallCount++;
                                if (wo > targetWeight) {
                                    targetWeight = wo;
                                    target = o.Flammable;
                                }
                            }
                        }
                    }
                    step = 10;

                    if (S.Beings.ByPos.TryGetValue(pos, out var beings)) {
                        for (int i1 = beings.Count - 1; i1 >= 0; i1--) {
                            step = 11;
                            Being b = beings[i1];
                            if (!b.IsActive || b.Flammable.IsOnFire) { continue; }
                            step = 12;
                            var wb = b.Flammable.AdjustFireSpreadWeight(posWeight);
                            if (wb > 0f) {
                                nonWallCount++;
                                if (wb > targetWeight) {
                                    targetWeight = wb;
                                    target = b.Flammable;
                                }
                            }
                        }
                    }
                    step = 13;

                    var nFlamCom = tileAt.Flammable;
                    step = 14;
                    if (nFlamCom.IsOnFire) { continue; }

                    step = 15;
                    if (tileAt.Definition.LayerId == WorldLayer.Walls) {
                        wallCount++;
                    } else {
                        nonWallCount++;
                    }
                    step = 16;
                    var w = nFlamCom.AdjustFireSpreadWeight(posWeight);
                    if (w > 0f && w > targetWeight) {
                        targetWeight = w;
                        target = nFlamCom;
                    }
                }
                step = 17;
                if (target == null) { return false; }
                if (nonWallCount == 0) {
                    // we can only spread into a wall, let's see if this is going to
                    // be allowed by the RNGsus
                    if (wallCount == 0 || !Rng.UChance(FireSpreadToWallChance)) {
                        return false;
                    }
                }
                step = 18;
                // Some surfaces are much harder to set on fire than others
                if (!Rng.UChance(target.SpreadIntoChance)) {
                    return false;
                }
                // TODO make fuel amt mul tunable instead of 0.5
                var newFire = fire.Spread(FireNewFlameStrengthAfterSpread, 0.5f);
                if (TrySetOnFire(target, newFire)) {
                    D.Warn("Spread fire into {0} ({1})", target,
                        Units.Percentage(target.SpreadIntoChance));
                    return true;
                } else {
                    return false;
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failure in TrySpreadFire: {0}", step);
                removeNow = true;
                return false;
            }
        }
        private readonly ErrorThrottle syncFlamErrors = new();
        private void AfterProcessFlammables(long ticks, in NativeList<int> changed) {
            #if DISABLE_FIREX
            return;
            #endif
            using (D.Timer("SyncFlams", 0.01f)) {
                int cnt = changed.Length;
                for (int i = 0; i < cnt; i++) {
                    // one flammable that threw used to leave every changed flammable after
                    // it unsynced (fire state, burn damage, visuals) on every tick
                    try {
                        SyncFlam(ticks, changed[i]);
                    } catch (System.Exception ex) {
                        if (syncFlamErrors.ShouldLog(changed[i], out var count)) {
                            D.LogEx(ex, "Exception in SyncFlam! ({0} times)", count);
                        }
                    }
                }
            }
            // the throttle holds its keys, so cleared flammables are let go daily (this runs
            // on ticks % 10 == 2)
            if (ticks % Consts.TicksPerDay == 2) {
                syncFlamErrors.Clear();
            }
            changed.Dispose();
        }

        private void SyncFlam(long ticks, int i) {
            if (!arrFlams.TryGet(i, out var flam)) { return; }
            if (!flam.NeedsSync) {
                D.ErrSoft("Flammable does not need sync! {0}", flam);
                return;
            }
            if (flamComps.TryGetValue(flam.Idx, out var comp)) {
                #if DEBUG_FIREX
                if (comp.Flam.Idx != flam.Idx) {
                    D.Err("Trying to update a flam component with wrong idx! expected {0}, was {1}. Old node: {2}. New node {3}",
                        flam.Idx, comp.Flam.Idx, comp.Flam, flam);
                }
                #endif
                if (comp.EntityId != flam.EntityId) {
                    D.Err("SyncFlam mismatch for comp / flam.comp id! {0} / {1}",
                        D.DumpInstance(flam), D.DumpInstance(comp));
                }
                // Entity can be destroyed after this call
                ApplyFlammableEffects(ticks, comp, ref flam);
                // Even if it was destroyed, must update
                comp.Update(flam);
            } else {
                D.Err("Node changed but there is no registered component! {0} -> {1}",
                    flam.Idx, flam);
            }
            if (flam.NeedsSync) {
                flam.NeedsSync = false;
                arrFlams.Update(i, in flam);
            }
            // arrFlams.Array[i] = flam;
        }

        private void ApplyFlammableEffects(long ticks, FlammableComp comp,
                ref Flammable flam) {
            #if DISABLE_FIREX
            return true;
            #endif
            bool wasSetOnFireNow = false;
            bool flamMayHaveChanged = false;

            if (flam.CurrentShouldTrySelfCombust && !comp.IsOnFire) {
                if (comp.CanSelfCombustAfter == 0 || comp.CanSelfCombustAfter < ticks) {
                    float fireChance = comp.ChanceToCatchOnFire;
                    if (comp.Being != null) {
                        var max = comp.Being.Data.StatsBeing.TempMax.Value;
                        if (max > flam.SelfCombustTempKelvin) {
                            fireChance = 0f;
                        }
                    }
                    if (Rng.UChance(fireChance)) {
                        wasSetOnFireNow = TrySetOnFire(comp, Fire.Create());
                        flamMayHaveChanged = wasSetOnFireNow;
                    }
                }
            }

            if (!wasSetOnFireNow) {
                // If we set the object on fire, let's not damage it at the same call,
                // because the we will try to mark it as set on fire while it just
                // got freshly destroyed
                if (flam.IsOnFire) {
                    if (DealFireDamageTo(comp)) {
                        flamMayHaveChanged = true;
                    }
                }
            }

            if (flamMayHaveChanged) {
                // refresh the flam
                flam = arrFlams.Array[flam.Idx];
            }
            flam.NeedsSync = false;
            flam.CurrentShouldTrySelfCombust = false;
        }

        public void Update(in FlammableComp c, in Flammable flam) {
            #if DISABLE_FIREX
            return;
            #endif
            if (!c.Entity.IsActive) {
                D.Err("Refusing to update flammable for inactive entity! {0}->{1}",
                    c, flam);
                return;
            }
            if (flam.EntityId != c.EntityId) {
                D.Err("Refusing to update flammable for: {0}->{1}. Entity id mismatch!",
                    c, flam);
                return;
            }
            if (c.SlotIdx != flam.Idx) {
                D.Err("Refusing to update flammable for: {0}->{1}. Flammable Idx mismatch!",
                    c, flam);
                return;
            }
            arrFlams.Update(flam.Idx, flam);
        }

        public override void Unload() {
            arrFlams?.Unload();
            arrFires?.Unload();
            FlammablesOnFire = 0;
            flamComps.Clear();
            fireParticles.Clear();
            fireSounds.Clear();
            fireLights.Clear();
        }

        private void TransferFireOnRemove(FlammableComp comp) {
            #if DISABLE_FIREX
            return;
            #endif
            int cnt = 1;
            if (comp.Tile != null && comp.Tile.Transform.IsMulti) {
                cnt = comp.Tile.Transform.Width * comp.Tile.Transform.Height;
            }
            var centerIdx = comp.Entity.CenterIdx;
            bool isOutOfMargin = !EntityUtils.IsWithinMargin(centerIdx, false, false);
            if (centerIdx == 0) {
                D.Err("Entity center idx is 0! {0}", comp.Entity);
                centerIdx = comp.Entity.PosIdx;
                D.Err("Entity pos idx is also 0! fuck!@!!");
            }
            if (centerIdx > 0 && !isOutOfMargin) {
                S.CmdQ.Enqueue(
                    new CmdSpawnMat(MatType.FastGet(MatIdH.Carbon), cnt,
                        comp.Entity.CenterIdx, 0.2f));
            }
            if (!arrFires.TryGet(comp.Flam.FireIdx, out var fire)) {
                D.Err("Could not get a fire from a flammable that was supposed to be on fire! {0}",
                    D.DumpInstance(comp));
                return;
            }
            if (!fire.IsValid) {
                // YOLO
                // if (comp.Entity.IsActive) {
                //     D.Err("Flammable was on fire but fire is invalid on removal! {0} | {1}",
                //         comp.Entity, D.DumpInstance(comp));
                // }
                return;
            }

            var src = comp.Entity;

            var newFire = fire.Copy();

            // Remove original fire
            RemoveFire(fire);

            if (newFire.Strength < 0.1f) {
                return;
            }

            if (isOutOfMargin) {
                return;
            }

            switch (comp.Entity.Definition.LayerId) {
                case WorldLayer.FreeEntities: {
                    // try obj first
                    var obj = EntityUtils.GetTopmostParentTile(comp.Entity.PosIdx);
                    if (obj != null && obj.IsConstructed && !obj.Flammable.IsOnFire) {
                        TrySetOnFire(obj, newFire);
                    }
                    break;
                }
                case WorldLayer.Objects: {
                    // if it's a multi tile object, we want to set every floor piece underneath on fire
                    if (comp.Tile.Transform.IsMulti) {
                        int count = comp.Tile.Transform.GetSubtilePos(Caches.PosIdx);
                        for (int i = 0; i < count; i++) {
                            var floor = EntityUtils.FloorAt(Caches.PosIdx[i]);
                            if (floor != null && floor.IsConstructed && !floor.Flammable.IsOnFire) {
                                TrySetOnFire(floor, newFire);
                            }
                        }
                    } else {
                        var floor = EntityUtils.FloorAt(comp.Entity.PosIdx);
                        if (floor != null && floor.IsConstructed && !floor.Flammable.IsOnFire) {
                            TrySetOnFire(floor, newFire);
                        }
                    }
                    break;
                }
                case WorldLayer.Walls: {
                    // try obj first
                    var floor = EntityUtils.FloorAt(comp.Entity.PosIdx);
                    if (floor != null && floor.IsConstructed && !floor.Flammable.IsOnFire) {
                        TrySetOnFire(floor, newFire);
                    }
                    break;
                }
                case WorldLayer.Floor: {
                    // Floor removed, fire should be gone
                    break;
                }
            }
        }

        public void RemoveFlammable(FlammableComp comp) {
            #if DISABLE_FIREX
            return;
            #endif
            if (comp.IsOnFire) {
                TransferFireOnRemove(comp);
                comp.SetOnFire(false);
            }

            var flam = comp.Flam;
            if (flam.Idx == -1) {
                D.Err("Trying to remove an unregistered flammable: {0}",
                    comp.Entity);
                return;
            }
            // get the most up to date version in case sync was pending:
            flam = arrFlams.Array[flam.Idx];
            #if DEBUG_FIREX
            D.Warn("Removing flammable {0}", flam);
            #endif

            if (!arrFlams.Remove(flam.Idx)) {
                D.Warn("Failed removing arrFlams by idx for entity: {0} : idx:{1}",
                    flam.EntityId, flam.Idx);
            }
            if (!flamComps.Remove(flam.Idx)) {
                D.Warn("Failed removing flamComps by idx for entity: {0} : idx:{1}",
                    flam.EntityId, flam.Idx);
            }
            flam.Idx = -1;
            comp.Flam = default;
            comp.SetSlot(-1);
        }

        public void AddFlammable(FlammableComp comp, bool allowUnbuilt = false) {
            #if DISABLE_FIREX
            return;
            #endif
            var flam = comp.Flam;
            #if DEBUG_FIREX
            D.Warn("Registering a flammable: {0}->{1}", comp, flam);
            #endif
            if (comp.Tile != null) {
                if (!allowUnbuilt && !comp.Tile.IsConstructed) {
                    D.Err("Trying to register an unbuilt tile! {0} -> {1}", flam, comp);
                    return;
                }
                if (comp.Health < 0.0001f) {
                    D.Err("Trying to register a node for a destroyed entity! {0}", comp.Entity);
                    return;
                }
            }
            if (!flam.IsValid) {
                comp.SetFlammableParams(ref flam);
                if (!flam.IsValid) {
                    D.Err("Trying to register an uninitialized node! {0} -> {1}", flam, comp);
                    return;
                }
            }
            var ntt = comp.Entity;
            if (arrFlams.TryReserveSlot(out var idx)) {
                flam.Idx = idx;
                arrFlams.Update(idx, flam);
                if (!flamComps.TryAdd(idx, comp)) {
                    D.Err("FlamComps already contains idx:{0}! Trying to overwrite {1} with {2}",
                        idx, flamComps[idx], comp);
                }
            } else {
                D.Err("Failed to reserve a slot for flammable: {0}", flam);
                idx = -1;
                flam.Idx = idx;
            }
            // comp.LoadFlammable(flam);
            comp.Flam = flam;
            comp.SetSlot(idx);
        }

        private FirexSysData data;

        public void SaveSpecial(SystemsDataSpecial sd) {
            #if DISABLE_FIREX
            return;
            #endif
            sd.Firex ??= data;
            sd.Firex.Flams = arrFlams.SaveData(out sd.Firex.FlamsCount,
                out sd.Firex.FlamsRecycler);
            sd.Firex.Fires = arrFires.SaveData(out sd.Firex.FiresCount,
                out sd.Firex.FiresRecycler);
            sd.Firex.FireBoostUntilTicks = Boost.UntilTicks;
            sd.Firex.FireCycle = FireCycle;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            #if DISABLE_FIREX
            return;
            #endif
            data = sd.Firex ?? new FirexSysData();
        }

        public void MigrateLegacyFlammables() {
            #if DISABLE_FIREX
            return;
            #endif
            // Legacy?
            var comps = S.Components.AllByType<FlammableComp>();
            if (comps == null) { return; }
            D.Warn("Migrating legacy flammables: {0}", comps.Count);
            foreach (FlammableComp comp in comps) {
                if (comp.Entity is Tile t) {
                    D.Warn("Tile: {0}. Constr: {1}. Is Constr: {2}", t, t.Constructable, t.IsConstructed);
                    if (!t.IsConstructed || t.Health.IsDestroyed) {
                        continue;
                    }
                } else if (comp.Entity is Obj o && o.Health.IsDestroyed) {
                    continue;
                }
                if (!comp.IsActive) {
                    AddFlammable(comp, true);
                } else {
                    D.Warn("Migrating legacy flammables, but this one is already registered: {0} -> {1}",
                        comp.Entity, D.DumpInstance(comp, true));
                }
            }
            ProcessFlammables(G.Ticks);
        }

        // Returns true if there there was a fire and it was extinguished
        public bool TryExtinguish(Entity entity) {
            #if DISABLE_FIREX
            return true;
            #endif
            if (entity == null) { return false; }
            if (entity is Tile t && t.Transform.IsChild) {
                entity = t.Transform.Parent;
            }
            var flam = entity.Flammable;
            if (flam == null) { return false; }
            // Read the live struct rather than flam.Flam (the component's cache) -
            // writing the cache back into arrFlams would clobber any array-only field
            // (e.g. FireCycle) the cache had gone stale on
            if (!arrFlams.TryGet(flam.SlotIdx, out var flamFlam)) { return false; }
            if (!flamFlam.IsValid) { return false; }
            if (arrFires.TryGet(flamFlam.FireIdx, out var fire)) {
                if (RemoveFire(fire)) {
                    flamFlam.TicksOnFire = 0;
                    flamFlam.FireIdx = -1;
                    arrFlams.Update(flamFlam.Idx, flamFlam);
                    S.Sig.FireStopped.Send(entity);
                    flam.Update(flamFlam);
                    return true;
                }
            }
            return false;
        }

        // Puts out every fire at a position (a tile, items and beings on it) at once.
        // Returns how many went out
        public int ExtinguishAt(int posIdx) {
            #if DISABLE_FIREX
            return 0;
            #endif
            int count = 0;
            for (int i = 0; i < arrFires.Count; i++) {
                if (!arrFires.TryGet(i, out var fire)) { continue; }
                if (!fire.IsValid || fire.PosIdx != posIdx) { continue; }
                if (flamComps.TryGetValue(fire.FlamIdx, out var flamComp)) {
                    if (TryExtinguish(flamComp.Entity)) {
                        count++;
                    }
                } else if (RemoveFire(fire)) {
                    // a stray fire without its flammable
                    count++;
                }
            }
            return count;
        }

        // Positions of the fires within radius of posIdx, center to center
        public void CollectFiresWithin(int posIdx, float radius, List<int> positions) {
            positions.Clear();
            #if DISABLE_FIREX
            return;
            #endif
            var radiusSqr = radius * radius;
            for (int i = 0; i < arrFires.Count; i++) {
                if (!arrFires.TryGet(i, out var fire)) { continue; }
                if (!fire.IsValid) { continue; }
                if (Pos.SqrDistance(posIdx, fire.PosIdx) <= radiusSqr) {
                    positions.Add(fire.PosIdx);
                }
            }
        }

        public bool TryGetFireAt(int posIdx, out Fire fire) {
            #if DISABLE_FIREX
            fire = default;
            return false;
            #endif
            if (posIdx == Pos.Invalid) {
                fire = default;
                D.Err("Trying to get fire at pos.invalid!");
                return false;
            }
            for (int i = 0; i < arrFires.Count; i++) {
                fire = arrFires.Array[i];
                if (fire.IsValid && fire.PosIdx == posIdx) {
                    return true;
                }
            }
            fire = default;
            return false;
        }

        public bool CheckIsOnFire(int posIdx) {
            #if DISABLE_FIREX
            return false;
            #endif
            if (posIdx == Pos.Invalid) {
                D.Err("Checking if pos.invalid is on fire!");
                return false;
            }
            for (int i = 0; i < arrFires.Count; i++) {
                #if FIREX_SAFE
                var fire = arrFires.Array[i];
                #else
                ref var fire = ref arrFires.Array.GetUnsafeRef(i);
                #endif
                if (fire.IsValid && fire.PosIdx == posIdx) {
                    return true;
                }
            }
            return false;
        }

        public Entity GetFireAt(int pos) {
            var topmostParent = EntityUtils.GetTopmostParentTile(pos);
            if (topmostParent?.Flammable.IsOnFire == true) {
                return topmostParent;
            }

            Fire foundFire = default;
            for (int i = arrFires.Count - 1; i >= 0; i--) {
                #if FIREX_SAFE
                if (!arrFires.TryGet(i, out var onF)) { continue; }
                #else
                ref var onF = ref arrFires.Array.GetUnsafeRef(i);
                #endif
                if (!onF.IsValid) { continue; }
                if (onF.PosIdx == pos) {
                    foundFire = onF;
                    break;
                }
            }
            if (foundFire.IsValid) {
                if (flamComps.TryGetValue(foundFire.FlamIdx, out var flamComp)) {
                    return flamComp.Entity;
                }
            }
            return null;
        }

        public Entity GetClosestReachableFireFor(Being worker,
                out float sqrDistToClosestFire) {
            #if DISABLE_FIREX
            return null;
            #endif
            float closestDist = float.MaxValue;
            sqrDistToClosestFire = float.MaxValue;
            Fire chosen = default;
            var pos = worker.PosIdx;
            bool needsIslandCheck = worker.Motion.IsGrounded
                && Ready.AreasInitial;
            var areas = S.Sys.Areas;
            float chanceOver2 = 0.12f;
            float chanceOver4 = 0.08f;
            float chanceOver8 = 0.02f;
            var ticks = G.Ticks;
            var isWorkerOOM = !EntityUtils.IsWithinMargin(worker.PosIdx, false, false);
            for (int i = arrFires.Count - 1; i >= 0; i--) {
                #if FIREX_SAFE
                if (!arrFires.TryGet(i, out var onF)) { continue; }
                #else
                ref var onF = ref arrFires.Array.GetUnsafeRef(i);
                #endif
                if (!onF.IsValid) { continue; }
                var fPos = onF.PosIdx;
                if (needsIslandCheck) {
                    if (!areas.IsSameIsland(pos, fPos)) { continue; }
                }
                var isFireOOM = !EntityUtils.IsWithinMargin(fPos, false, false);
                // ignore fire on derelict unless you are on derelict
                if (isFireOOM && !isWorkerOOM) {
                    continue;
                }
                if (!IsWorthExtinguishing(onF.FlamIdx)) {
                    continue;
                }
                bool canTake = true;
                if (Firefight.HasEnoughFirefightersFor(worker, fPos)) {
                    // this could still be a fallback
                    canTake = false;
                    continue;
                }
                if (worker.Motion.HadNavFailureAt(fPos)) {
                    canTake = false;
                    continue;
                }
                if (!worker.Data.Safety.IsActuallySurvivableAt(fPos)) {
                    canTake = false;
                    continue;
                }
                var dist = Pos.SqrDistance(pos, onF.PosIdx);
                if (dist < sqrDistToClosestFire) {
                    sqrDistToClosestFire = dist;
                }
                if (canTake && dist < closestDist) {
                    closestDist = dist;
                    chosen = onF;
                    // Make worker go slightly longer distance if there's a lot of
                    // firefighters, so everyone won't go to the same exact spot
                    // every single time
                    if (dist < 2 * 2 && S.Rng.Chance(chanceOver2)) {
                        break;
                    } else if (dist < 4 * 4 && S.Rng.Chance(chanceOver4)) {
                        break;
                    } else if (dist < 8 * 8 && S.Rng.Chance(chanceOver8)) {
                        break;
                    }
                }
            }
            if (chosen.IsValid) {
                if (flamComps.TryGetValue(chosen.FlamIdx, out var flam)) {
                    //D.Warn("Got closest flame for: {0} -> {1}", worker,
                        //Pos.String(flam.Entity.PosIdx));
                    if (flam.Entity is Being b) {
                        if (b.CanFight
                                && b.Data.EffectiveFaction != worker.Data.EffectiveFaction
                                && (S.Sys.Combat.IsHostileCombatInProgress || b.Species.IsWild)) {
                            // don't extinguish enemies or crawlers
                            return null;
                        }
                    }
                    return flam.Entity;
                } else {
                    // We failed to get the component, it means this fire is
                    // invalid
                    D.ErrSoft("Removing stray fire: {0}", chosen);
                    RemoveFire(chosen);
                }
            }
            //D.Warn("Could not get closest flame for: {0}", worker);
            return null;
        }

        public bool AddFirefightingEffortFor(FlammableComp flam, float amt) {
            #if DISABLE_FIREX
            return true;
            #endif
            if (flam.IsOnFire && arrFires.TryGet(flam.Flam.FireIdx, out var fire)) {
                fire.Strength -= amt * Boost.FirefightingMulFor(flam);
                arrFires.Update(fire.Idx, fire);
                return true;
            }
            return false;
        }

        public bool AddFirefightingEffortAt(int currentFirePos, float amt) {
            #if DISABLE_FIREX
            return true;
            #endif
            for (int i = 0; i < arrFires.Count; i++) {
                #if FIREX_SAFE
                var fire = arrFires.Array[i];
                #else
                ref var fire = ref arrFires.Array.GetUnsafeRef(i);
                #endif
                if (fire.IsValid && fire.PosIdx == currentFirePos) {
                    flamComps.TryGetValue(fire.FlamIdx, out var flam);
                    fire.Strength -= amt * Boost.FirefightingMulFor(flam);
                    arrFires.Update(fire.Idx, fire);
                    return true;
                }
            }
            return false;
        }

        private bool AddFireAmbience(in Fire fire) {
            #if DISABLE_FIREX
            return true;
            #endif
            if (fireSounds.TryGetValue(fire.Idx, out var amb)) {
                D.Err("Fire ambience is already playin: {0}", D.DumpInstance(fire));
                return false;
            }

            if (!fire.CanMove && OnFireCount > 20) {
                if (!Rng.UChance(Maths.Max(5f / OnFireCount, 0.1f))) {
                    return false;
                }
            }

            var ambienceData = new AudioSys.AmbienceData() {
                ClipId = ambienceId,
                Id = ambienceIdHash,
                Rolloff = ambienceRolloff,
                Position = fire.Position,
                Volume = 1f,
                Pitch = 1f,
                Owner = "fire",
                Play = true,
            };
            if (fireSounds.TryAdd(fire.Idx, ambienceData)) {
                S.Sys.Audio.AddAmbience(ambienceData);
                return true;
            }
            return false;
        }

        private bool RemoveFireAmbience(in Fire fire) {
            #if DISABLE_FIREX
            return true;
            #endif
            if (fireSounds.TryGetValue(fire.Idx, out var amb)) {
                S.Sys.Audio.RemoveAmbience(amb);
                fireSounds.Remove(fire.Idx);
                return true;
            }
            // we don't always add the ambience
            //D.Err("Failed to remove fire ambience: {0}", D.DumpInstance(fire));
            return false;
        }

        public bool TryGetFireFor(FlammableComp flammableComp, out Fire fire) {
            #if DISABLE_FIREX
            fire = default;
            return false;
            #endif
            if (flammableComp.Flam.IsOnFire) {
                return arrFires.TryGet(flammableComp.Flam.FireIdx, out fire);
            }
            fire = default;
            return false;
        }
    }
}

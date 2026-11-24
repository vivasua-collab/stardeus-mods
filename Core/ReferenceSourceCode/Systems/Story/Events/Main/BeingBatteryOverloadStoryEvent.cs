using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.AI;

namespace Game.Systems.Story {
    public sealed class BeingBatteryOverloadStoryEvent : StoryEvent {
        public const string EventId = "BeingBatteryOverload";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new BeingBatteryOverloadStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var alive = ctx.S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                Being a = alive[i];
                if (!a.Species.IsBiological) {
                    return true;
                }
            }
            return false;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var S = ctx.S;
            var ticks = S.Ticks;
            var alive = S.Beings.Alive;
            if (alive.Count == 0) {
                data.FailEvent(S, "No beings in the map", isHardFailure: false);
                return;
            }
            var drones = new List<Being>();
            for (int i = 0; i < alive.Count; i++) {
                Being a = alive[i];
                if (a.Species.IsBiological) { continue; }
                if (a.AIAgent.Dockable != null && a.AIAgent.Dockable.IsDocked) {
                    continue;
                }
                if (a.AIAgent.CurrentAction?.IdH == AIActionRecharge.ActIdH) {
                    continue;
                }
                if (a.AIAgent.CurrentAction?.IdH == AIActionUseRepairStation.ActIdH) {
                    continue;
                }
                // don't affect newly produced drones
                if (a.Data.Body.BirthTick < ticks - Consts.TicksPerMonth) {
                    if (a.Data.Body.HasBodyPart(BodyPartIdH.Battery)) {
                        #if !PRODUCTION_BUILD
                        D.Log("Enqueueing drone with age: {0} -> {1}",
                            Units.TicksToAge(ticks - a.Data.Body.BirthTick), a);
                        #endif
                        drones.Add(a);
                    }
                }
            }
            var target = data.SeededRng.From(drones);
            if (target == null) {
                data.FailEvent(S, "Couldn't find a target for battery overload", isHardFailure: false);
                return;
            }

            data.TargetBeing = target.Data;
            TriggerOverloadEvent(ctx, data, target);
            data.SetState(S, StoryEventState.Ending);
        }

        private void TriggerOverloadEvent(StoryContext ctx, StoryEventData data, Being target) {
            var S = ctx.S;
            var rng = data.SeededRng;

            List<int> batterySlots = new();
            foreach (var slot in target.Data.Body.Slots) {
                if (slot.Type.IdH == BodyPartIdH.Battery
                        && !slot.IsPartRemoved) {
                    batterySlots.Add(slot.IdH);
                }
            }

            if (batterySlots.Count == 0) {
                D.Warn("No battery slot for being: {0}", target);
                data.FailEvent(S, "No battery slot for being", isHardFailure: false);
                return;
            }

            var targetSlotId = rng.From(batterySlots);
            var source = NamedEntity.For(T.EvtDroneBattExplodedTitle);
            BeingUtils.DamageBodyPart(S, target.Data, targetSlotId, rng,
                DamageType.Energy, Severity.Critical, source, 0f);

            if (S.Difficulty.Level > 1.5f &&
                    Explosions.CanExplode(0.2f, rng)) {
                Explosion.Create(S, 2, position: target.Position);
                Explosion.Create(S, 2, position: target.Position,
                    fireChance: 0.9f,
                    chemFuelAmt: 0.2f,
                        flags: ExplosionFlags.FireOnly);

                data.CreateLogEntry(
                    IconId.CExplosion,
                    T.EvtDroneBattExplodedTitle,
                    T.EvtDroneBattExplodedDesc_V1.F(Texts.LinkToBeing(target.Data))
                );
                data.ShowPopup();

                var title = Texts.Red(T.EvtDroneBattExplodedTitle);
                S.Sys.Log.AddLine(target, title);
                S.Sig.ShowFanfare.Send(Texts.LinkToBeing(target.Data, title));
                return;
            }

            if (S.Difficulty.Level < 1.5f || rng.Chance(0.5f)) {
                SetBatteryOnFire(data, target, S);
            } else {
                var found = S.Beings.FindInRadius(Caches.BeingList, target.Position, 3f);
                var beings = Caches.BeingList;
                // TODO add visual effect for shock damage
                var affected = new List<string>();
                for (int i = found - 1; i >= 0; i--) {
                    Being b = beings[i];
                    if (b.IsAlive) {
                        affected.Add(Texts.LinkToBeing(b.Data));
                        BeingUtils.DamageRandomBodyPart(S, b.Data, rng,
                            DamageType.Energy, Severity.Moderate,
                                target.AIAgent.CombatUnit, 0.5f);
                    }
                }
                if (affected.Count > 1) {
                    data.CreateLogEntry(
                        IconId.CEnergy,
                        T.EvtDroneBattElectrocutedTitle_V1.F(affected.Count),
                        T.EvtDroneBattElectrocutedDesc_V2.F(
                            Texts.LinkToBeing(target.Data),
                            string.Join(", ", affected))
                    );
                    data.ShowPopup();
                    var title = Texts.Red(T.EvtDroneBattElectrocutedTitle_V1.F(affected.Count));
                    S.Sys.Log.AddLine(target, title);
                    S.Sig.ShowFanfare.Send(title);
                } else {
                    // fallback
                    SetBatteryOnFire(data, target, S);
                }
            }
        }

        private static void SetBatteryOnFire(StoryEventData data, Being target, GameState S) {
            S.Sys.Log.AddLine(target, T.EvtDroneBattFireTitle);
            S.Sys.Firex.TrySetOnFire(target, Fire.CreateBalanced(S, str: 1.2f, fuel: 1f));

            data.CreateLogEntry(
                IconId.CFire,
                T.EvtDroneBattFireTitle,
                T.EvtDroneBattFireDesc_V1.F(Texts.LinkToBeing(target.Data))
            );
            data.ShowPopup();

            var title = Texts.Red(T.EvtDroneBattFireTitle);
            S.Sig.ShowFanfare.Send(Texts.LinkToBeing(target.Data, title));
        }
    }
}

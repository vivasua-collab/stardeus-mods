using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI;

namespace Game.Systems.Story {
    public sealed class BeingBatteryDischargeStoryEvent : StoryEvent {
        public const string EventId = "BeingBatteryDischarge";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new BeingBatteryDischargeStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var foundCount = 0;
            var alive = ctx.S.Beings.Alive;
            for (int i = 0; i < alive.Count && foundCount == 0; i++) {
                Being b = alive[i];
                if (b.Data.HasStateFlag(Creatures.BeingState.InDevice)) {
                    continue;
                }
                if (b.Data.StatsBeing.BatteryCharge.Value > 20
                        // don't affect newly produced drones
                        && b.Data.Body.BirthTick < ctx.S.Ticks - Consts.TicksPerWeek) {
                    if (b.AIAgent.Dockable != null && b.AIAgent.Dockable.IsDocked) {
                        continue;
                    }
                    if (b.AIAgent.CurrentAction?.IdH == AIActionRecharge.ActIdH) {
                        continue;
                    }
                    if (b.AIAgent.CurrentAction?.IdH == AIActionUseRepairStation.ActIdH) {
                        continue;
                    }
                    foundCount++;
                }
            }
            return foundCount > 0;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var S = ctx.S;
            var beings = new List<Being>();
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                Being b = alive[i];
                if (b.Data.StatsBeing.BatteryCharge.Value > 20
                        // don't affect newly produced drones
                        && b.Data.Body.BirthTick < S.Ticks - Consts.TicksPerWeek) {
                    #if !PRODUCTION_BUILD
                    D.Log("Enqueueing drone with age: {0} -> {1}",
                        Units.TicksToAge(S.Ticks - b.Data.Body.BirthTick), b);
                    #endif
                    if (b.AIAgent.Dockable != null && b.AIAgent.Dockable.IsDocked) {
                        continue;
                    }
                    if (b.AIAgent.CurrentAction?.IdH == AIActionRecharge.ActIdH) {
                        continue;
                    }
                    if (b.AIAgent.CurrentAction?.IdH == AIActionUseRepairStation.ActIdH) {
                        continue;
                    }
                    beings.Add(b);
                }
            }
            if (beings.Count > 0) {
                var selectedBeing = data.SeededRng.From(beings);
                CreateDischargeEvent(ctx, data, selectedBeing);
                data.SetState(S, StoryEventState.Ending);
            } else {
                data.FailEvent(S, "No beings eligible for battery discharge", isHardFailure: false);
            }
        }

        private void CreateDischargeEvent(StoryContext ctx, StoryEventData data, Being being) {
            var S = ctx.S;
            data.TargetBeing = being.Data;

            data.CreateLogEntry(
                IconId.CEnergy,
                T.EvtBatteryDischargeTitle,
                T.EvtBatteryDischargeDesc_V1.F(Texts.LinkToBeing(being.Data))
            );
            data.ShowPopup();

            being.Data.StatsBeing.BatteryCharge.UpdateBase(0f);
            var title = Texts.Yellow(T.EvtBatteryDischargeTitle);
            S.Sys.Log.AddLine(being, title);
            S.Sig.ShowFanfare.Send(Texts.LinkToBeing(being.Data, title));
        }
    }
}

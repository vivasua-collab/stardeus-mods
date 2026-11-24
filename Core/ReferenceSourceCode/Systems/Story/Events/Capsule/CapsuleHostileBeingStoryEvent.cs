using System.Collections.Generic;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Constants;
using Game.Utils;
using Game.Input;

namespace Game.Systems.Story {
    public class CapsuleHostileBeingStoryEvent : StoryEvent {

        public const string EventId = "CapsuleHostileBeing";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleHostileBeingStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var obj = data.TargetObj?.Obj;
            if (obj == null) {
                D.Err("StoryEventData did not have TargetObj: {0} | {1}", data, this);
                data.FailEvent(ctx.S, "StoryEventData has no TargetObj");
                return;
            }
            var showPopup = !ctx.S.Sys.Combat.IsHostileAlertUp;

            // Raiders generated up front by the multi-capsule event (see RaidPayload): this
            // capsule only carries them, so release instead of running a raid of its own.
            var raiderIds = data.Data?.GetIntSet("RaiderIds", null);
            if (!Arrays.IsEmpty(raiderIds)) {
                data.Faction ??= ctx.S.Sys.Diplomacy.RandomFactionForRaid();
                var released = new List<BeingData>();
                var payloadArmy = RaidPayload.Release(ctx.S, raiderIds, obj.PosIdx,
                    data.Faction, released);
                if (payloadArmy != null && released.Count > 0) {
                    data.SetState(ctx.S, StoryEventState.Ending);
                    var first = released[0];
                    var payloadTitle = $"{CapsuleOpenHelper.ContainerName(data)} {GlyphMap.ArrowRight} {T.Hostile}: {first.Species.Name}";
                    data.TargetBeing = first;
                    data.CreateLogEntry(first.Being.Definition.Preview, payloadTitle,
                        CapsuleOpenHelper.ContainedText(data, first.Mind.CombatLogName));
                    if (showPopup) {
                        data.ShowPopup();
                    }
                    return;
                }
                D.Err("Failed releasing raid payload from capsule!", data, this);
                data.FailEvent(ctx.S, "Failed to release raid payload");
                return;
            }

            var raid = new CombatRaid(ctx.S, data.Seed);
            raid.MaxRaiders = 1;
            data.Faction ??= ctx.S.Sys.Diplomacy.RandomFactionForRaid();
            raid.Faction = data.Faction;
            raid.PosIdx = obj.PosIdx;
            var raidPoints = data.Data?.GetFloat("RaidPoints", 0f) ?? 0f;
            if (raidPoints > 0f) {
                raid.PresetPoints = raidPoints;
                raid.PresetGearTier = data.Data?.GetInt("RaidGearTier", -1) ?? -1;
            }
            var army = raid.PrepareArmy();
            if (army != null) {
                var being = Arrays.SafeGetFast(army.Units, 0);
                army = ctx.S.Query.StartRaid.Ask(army);
                if (army != null) {
                    data.SetState(ctx.S, StoryEventState.Ending);
                    // var title = $"{T.Hostile}: {Texts.LinkToBeing(being.Data, Texts.Yellow(being.Data.Species.Name))}";
                    var title = $"{CapsuleOpenHelper.ContainerName(data)} {GlyphMap.ArrowRight} {T.Hostile}: {being.Data.Species.Name}";
                    data.TargetBeing = being.Data;
                    data.CreateLogEntry(
                        being.Agent.Being.Definition.Preview,
                        title,
                        CapsuleOpenHelper.ContainedText(data, being.Data.Mind.CombatLogName));
                    if (showPopup) {
                        data.ShowPopup();
                    }
                    return;
                }
            }
            D.Err("Failed starting a raid!", data, this);
            data.FailEvent(ctx.S, "Failed to start a raid");
        }
    }
}

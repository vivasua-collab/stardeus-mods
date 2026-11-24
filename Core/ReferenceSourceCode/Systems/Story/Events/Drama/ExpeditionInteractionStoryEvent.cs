using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.CodeGen;
using UnityEngine;
using Game.Utils;
using Game.Systems.Drama;
using Game.Systems.Quests;
using System;
using KL.Utils;
using System.Linq;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Grid;
using Game.Input;
using Game.Research;

namespace Game.Systems.Story {
    public sealed class ExpeditionInteractionStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new ExpeditionInteractionStoryEvent());
        }

        public const string EventId = "ExpeditionInteraction";
        public override string Id => EventId;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Sys.Expedition.Expeditions.Count > 0;
        }
        public override bool CheckHook(StoryContext ctx, StoryEventDef def, StoryEventHook hook) {
            if (hook.Target is not List<BeingCluster> clusters) {
                D.Warn("Hook target not list of being clusters! {0}", hook.Target);
                return false;
            }
            foreach (var cl in clusters) {
                if (IsClusterCompatible(ctx, def, cl)) {
                    return true;
                }
            }
            return false;
        }

        private bool IsClusterCompatible(StoryContext ctx, StoryEventDef def,
                BeingCluster cluster) {
            if (!cluster.IsExpedition) { return false; }
            if (cluster.Expedition.StageData.GetBool(
                    ShareExpeditionWithBeingQuestStep.SuppressExpeditionSocialEventsKey, false)) {
                return false;
            }
            if (cluster.FriendlySociable.Count < 2) {
                return false;
            }
            var stageType = def.Vars.Get(PropertyIdH.CompatibleStage, string.Empty);
            var startTick = cluster.Expedition.StartTick;
            if (stageType != string.Empty) {
                if (cluster.Expedition.StageType != Hashes.S(stageType)) {
                    return false;
                }
                startTick = cluster.Expedition.StageStartTick;
            }

            var ticksInState = ctx.Ticks - startTick;
            var minHoursInStage = def.Vars.Get(PropertyIdH.MinHoursInStage, 3f);
            if (ticksInState < minHoursInStage * Consts.TicksPerHour) {
                // too soon
                return false;
            }

            var reqDepositWithDepth = def.Vars.Get(PropertyIdH.RequireDepositWithDepth, false);
            if (reqDepositWithDepth) {
                if (!ctx.S.Sys.Planets.TryGetResourceFor(
                        cluster.Expedition.Target, out var resource, out var mt)) {
                    return false;
                }
                if (mt.Deposit == null) {
                    D.Err("Expedition to a material type without deposit config! {0}", mt);
                    return false;
                }
                if (!mt.Deposit.IsWithDepth) {
                    return false;
                }
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.TargetOther is not List<BeingCluster> clusters) {
                data.FailEvent(s,
                    $"TargetOther is not a list of being clusters: {data.TargetOther}");
                return;
            }
            var rng = data.SeededRng;
            var cluster = clusters.Where(cl => IsClusterCompatible(ctx, data.Def, cl))
                .OrderBy(cl => rng.Value).FirstOrDefault();

            var first = cluster.FriendlySociable.OrderBy(b => rng.Value)
                .FirstOrDefault(b => b.HasStateFlag(BeingState.Alive));
            if (first == null) {
                data.FailEvent(s, "Couldn't find first participant", isHardFailure: false);
                return;
            }
            var second = cluster.FriendlySociable.Where(b => b != first).OrderBy(b => rng.Value)
                .FirstOrDefault(b => b.HasStateFlag(BeingState.Alive));
            if (second == null) {
                data.FailEvent(s, "Couldn't find second participant", isHardFailure: false);
                return;
            }

            data.AddParticipant(first);
            data.AddParticipant(second);
            data.TargetSO = cluster.Expedition.Target;
            data.Location = A.PlayerSector?.Sector;
            data.SetState(s, StoryEventState.Ending);

            ApplyEffects(ctx, data, first, second, cluster.Expedition);
        }

        private void ApplyEffects(StoryContext ctx, StoryEventData data,
                BeingData b1, BeingData b2, Expedition exp) {
            bool showFanfare = data.Def.Vars.Get(PropertyIdH.ShowFanfare, false);
            bool addLog = data.Def.Vars.Get(PropertyIdH.AddLog, true);
            string titleKey = data.Def.Vars.Get(PropertyIdH.TitleKey, string.Empty);
            string textKey = data.Def.Vars.Get(PropertyIdH.TextKey, string.Empty);
            string icon = data.Def.Vars.Get(PropertyIdH.Icon, string.Empty);
            float attChange = data.Def.Vars.Get(PropertyIdH.AttitudeChange, 0f);
            float loveChange = data.Def.Vars.Get(PropertyIdH.LoveChange, 0f);
            float friendshipChange = data.Def.Vars.Get(PropertyIdH.FriendshipChange, 0f);
            float hateChange = data.Def.Vars.Get(PropertyIdH.HateChange, 0f);
            float changeWeightB1 = data.Def.Vars.Get(PropertyIdH.ChangeWeightB1, 1f);
            float changeWeightB2 = data.Def.Vars.Get(PropertyIdH.ChangeWeightB2, 1f);
            bool showPopup = data.Def.Vars.Get(PropertyIdH.ShowPopup, false);
            bool addEvent = data.Def.Vars.Get(PropertyIdH.AddEventNotification, false);
            bool addRandomLoot = data.Def.Vars.Get(PropertyIdH.AddRandomLoot, false);

            var text = textKey.T(b1.Mind.LinkToShortName, b2.Mind.LinkToShortName);

            if (addRandomLoot) {
                var ids = Caches.WeightedStrings;
                ids.Clear();
                var market = ctx.S.Market;
                foreach (var def in The.Defs.Defs.Values) {
                    if (def.IsAbstract || def.IsBeing || def.IsTemplate ||
                            def.IgnoreMarket ||
                            def.IsConstructable || Researchable.TreeNode(def) ||
                            def.Tier < 1) {
                        continue;
                    }
                    if (def.HasComponent(ComponentIdH.Plant)) {
                        continue;
                    }

                    var mi = market.Find(def.IdH);
                    if (mi == null) { continue; }
                    if (Arrays.ContainsSafe(mi.Item.GroupsH, MarketGroupIdH.Ore)) {
                        continue;
                    } if (Arrays.ContainsSafe(mi.Item.GroupsH, MarketGroupIdH.Food)) {
                        continue;
                    }
                    if (Arrays.ContainsSafe(mi.Item.GroupsH, MarketGroupIdH.Flora)) {
                        continue;
                    }

                    ids.Add(def.Id, 0.1f / def.Tier);
                }
                if (data.SeededRng.TryWeightedFrom(ids, out var id)) {
                    exp.AddObjLoot(id);
                    var def = The.Defs.Get(id);
                    text = $"{text}{GlyphMap.BRHR}{T.ExpeditionLoot}: {def.CodexLink}";
                } else {
                    D.Err("Failed picking random loot for mining expedition! {0}", data);
                    exp.AddObjLoot(DefIds.ObjEggsEgg01);
                }
            }

            data.CreateLogEntry(icon, titleKey.T(), text);

            ApplyMentalEffect(ctx, data, b1, PropertyIdH.MentalEffectB1,
                PropertyIdH.MentalEffectDurationHoursB1, data.LogEntry.Desc);
            ApplyMentalEffect(ctx, data, b2, PropertyIdH.MentalEffectB2,
                PropertyIdH.MentalEffectDurationHoursB2, data.LogEntry.Desc);

            if (addEvent) {
                data.AddEventNotification(ctx, data.LogEntry.Icon,
                    data.LogEntry.Title, showPopup: showPopup);
            } else if (showPopup) {
                data.ShowPopup();
            }
            if (showFanfare) {
                ctx.S.Sig.ShowFanfare.Send(data.LogEntry.Desc);
            }
            if (addLog) {
                ctx.S.Sys.Log.AddLine(data.LogEntry.Desc);
            }

            var soc = SocialInteraction.For(data.LogEntry)
                .With(b1, changeWeightB1)
                .With(b2, changeWeightB2)
                .WithAttitude(attChange)
                .WithLove(loveChange)
                .WithHate(hateChange)
                .WithFriendship(friendshipChange);
            ctx.S.Query.RegisterSocialInteraction.Ask(soc);
        }

        private void ApplyMentalEffect(StoryContext ctx, StoryEventData data,
                BeingData bd, int effectVar, int durVar, string tip) {
            if (bd.Mind.Health == null) { return; }
            var change = data.Def.Vars.Get(effectVar, 0);
            if (change == 0) { return; }
            float dur = data.Def.Vars.Get(durVar, 0f);
            if (dur <= 0f) {
                D.Err("{1} not set in story event config: {0}",
                    data.Def, PropertyIdH.ResolveName(durVar));
                dur = 1f;
            }
            var eff = MentalEffect.CreateWithIcon(
                Pos.Invalid, data.LogEntry.Icon, G.Ticks,
                (int) (dur * Consts.TicksPerHour),
                data.LogEntry.Title, change)
                    .WithTooltip(tip);
            bd.Mind.Health.AddEffect(ctx.S, eff);
        }
    }
}

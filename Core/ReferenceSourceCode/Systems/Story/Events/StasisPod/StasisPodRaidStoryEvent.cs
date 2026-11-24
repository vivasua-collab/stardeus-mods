using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Notify;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI.Combat;
using Game.Systems.Naming;
using Game.Systems.Voice;
using Game.Input;

namespace Game.Systems.Story {
    public sealed class StasisPodRaidStoryEvent : StoryEvent {
        public const string EventId = "StasisPodRaid";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new StasisPodRaidStoryEvent());
        }
        // One pod has one outport: a thematic ambush, not an all-out attack. Capped and
        // discounted so a late-game colony doesn't get 40 raiders out of a single pod, and
        // not counted toward NumRaids (see CombatRaid.CountsAsRaid).
        private const int MaxPodRaiders = 12;
        private const float PodStrengthMul = 0.6f;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn;
        }

        private void CreateLogEntry(StoryEventData data, FactionDef raidingFaction) {
            data.CreateLogEntry(raidingFaction.FlagIcon,
                    T.EvtStasisPodRaidTitle_V1.F(raidingFaction.NameT),
                    T.EvtStasisPodRaidDesc_V1.F(raidingFaction.NameT));
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);

            var raidingFaction = s.Sys.Diplomacy.RandomFactionForRaid();
            var rng = new Rng(pod.ContentsSeed);
            var spawnPos = EntityUtils.SafeSpawnPosFor(pod.Tile, rng.Fork());

            var raid = new CombatRaid(s, pod.ContentsSeed);
            raid.Faction = raidingFaction;
            raid.PosIdx = pod.OutPort;
            raid.MinRaiders = 1;
            raid.MaxRaiders = MaxPodRaiders;
            raid.AdjustStrength(PodStrengthMul);
            raid.CountsAsRaid = false;

            if (raid.TryStartRaid()) {
                Notification.Build(raidingFaction.FlagIcon, T.EvtStasisPodRaidTitle_V1.F(raidingFaction.NameT),
                        Consts.TicksPer4Hours, NotificationType.Alert)
                    .WithOnClick(data.ShowPopup)
                    .Emergency(true)
                    .Show();
            } else {
                EntityUtils.SpawnExplosive(raidingFaction, pod.OutPort, rng);
            }

            pod.Open();
            CreateLogEntry(data, raidingFaction);
            s.Sig.ShowFanfare.Send(Texts.LinkToTile(pod.Tile,
                Texts.Yellow(T.EvtStasisPodRaidTitle_V1.F(raidingFaction.NameT))));
            s.Sys.Log.AddLine(pod.Tile, T.EvtStasisPodRaidTitle_V1.F(raidingFaction.NameT));
        }
    }
}

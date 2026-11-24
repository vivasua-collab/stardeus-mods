using System.Collections.Generic;
using Game.Achievements;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Naming;
using Game.Systems.Items;
using Game.Data.Factions;

namespace Game.Systems.Story {
    public sealed class SpawnSurvivorsOnShuttleLandStoryEvent : StoryEvent {
        public const string EventId = "SpawnSurvivorsOnShuttleLand";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new SpawnSurvivorsOnShuttleLandStoryEvent());
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            if (data.TargetTile == null || !data.TargetTile.IsActive) {
                data.FailEvent(ctx.S, "Must have active shuttle set on TargetTile!");
                return;
            }
            if (!data.TargetTile.TryGetComponent<ShuttleComp>(out var shuttle)) {
                data.FailEvent(ctx.S, "TargetTile not a shuttle!");
                return;
            }
            if (shuttle.IsOut) { return; }

            int survivorCount = Mathf.Max(1, data.SeededRng.Range(1, 3));

            CmdSpawnBeing cmd = null;
            var species = Species.Get(SpeciesIdH.Human);
            var spawnPoint = shuttle.Tile.Transform.WorkSpotOrPos;
            var bd = Caches.BeingDataList;
            bd.Clear();
            for (int i = 0; i < survivorCount; i++) {
                var bVars = new GenBeingVars(species, BeingType.Friendly);
                bVars.Body = FactionDef.RandomBodyVars(data.SeededRng);
                var bData = BeingUtils.GenBeingWithInventory(bVars, ctx.S.Rng,
                    GenItemFlags.Casual | GenItemFlags.SpaceReady);

                cmd = new CmdSpawnBeing(bData, spawnPoint);
                cmd.Execute(ctx.S);
                if (cmd.SpawnedBeing != null) {
                    BeingUtils.ShowColonyGreetingFor(ctx.S, cmd.SpawnedBeing);
                    bd.Add(cmd.SpawnedBeing.Data);
                }
            }
            ctx.S.Query.GenerateRelationshipsFor.Ask(bd);
            var being = cmd?.SpawnedBeing;
            if (being == null) {
                data.FailEvent(ctx.S, $"Failed spawning human at shuttle position: {shuttle.Entity.Position}");
                return;
            }
            data.TargetBeing = being.Data;
            data.HideLogEntry();
            data.SetState(ctx.S, StoryEventState.Ending);
            // Log entry and fanfare
            var title = Texts.Green(T.EvtExpCrashSiteSurvivorsGoodTitle);
            ctx.S.Sys.Log.AddLine(shuttle.Tile, title);
            ctx.S.Sig.ShowFanfare.Send(Texts.LinkToTile(shuttle.Tile, title));
        }
    }
}

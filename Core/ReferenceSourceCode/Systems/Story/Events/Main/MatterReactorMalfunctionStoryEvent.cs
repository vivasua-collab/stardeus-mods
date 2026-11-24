using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;
using Game.Systems.Story;
using Game.Systems.Voice;

namespace Game.Systems.Story {
    public sealed class MatterReactorMalfunctionStoryEvent : StoryEvent {
        public const string EventId = "MatterReactorMalfunction";
        public override string Id => EventId;
          [SelfInit]
        private static void Register() {
            Register(new MatterReactorMalfunctionStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var rng = data.SeededRng;

            // Find active matter reactors that are producing power
            var numFound = s.Components.FindAllMatching(Caches.ElectricNodes256,
                k => k.IsSwitchedOn &&
                    k.Node.Produced > 0 &&
                    !k.Tile.Definition.IsImportant &&
                    k.Entity.HasComponent<MatterReactorComp>());

            if (numFound == 0) {
                data.FailEvent(s, "No matter reactors found", isHardFailure: false);
                return;
            }

            // If we have too many, select the ones most likely to fail
            if (numFound > 10) {
                // Sort by break chance (highest first) and take top 10
                var sortedNodes = new List<ElectricNodeComp>();
                for (int i = 0; i < numFound; i++) {
                    sortedNodes.Add(Caches.ElectricNodes256[i]);
                }
                sortedNodes.Sort((a, b) =>
                    b.Tile.GetComponent<WithFuseComp>().CurrentBreakChance
                    .CompareTo(a.Tile.GetComponent<WithFuseComp>().CurrentBreakChance));

                for (int i = 0; i < 10; i++) {
                    Caches.ElectricNodes256[i] = sortedNodes[i];
                }
                numFound = 10;
            }

            var randomNode = Caches.ElectricNodes256[rng.Range(0, numFound)];

            if (randomNode.Tile.TryGetComponent<WithFuseComp>(out var withFuse)) {
                if (withFuse.TryBlowFuse()) {
                    DeviceMalfunctionStoryEvent.CreateMalfunctionEvent(data, randomNode.Tile, true);
                    data.SetState(s, StoryEventState.Ending);
                    return;
                }
            } else {
                D.Err("No WithFuseComp on {0}", randomNode.Tile);
            }

            data.TargetTile = randomNode.Tile;
            var wt = s.Difficulty.FireChanceScale;
            string ending;

            if (Explosions.CanExplode(wt * 0.1f, rng)) {
                // Explode
                randomNode.Tile.Flammable.CreateExplosion(false);
                ending = T.EvtEndExplosion;
            } else {
                // Start fire
                Explosion.Create(s, randomNode.Tile.Flammable.ExplosionLevel,
                    position: randomNode.Entity.Position,
                    fireChance: 0.9f,
                    chemFuelAmt: 0.05f,
                    flags: ExplosionFlags.FireOnly);
                ending = T.EvtEndFire;
            }

            var name = randomNode.Entity.Definition.CodexLink;
            data.CreateLogEntry(randomNode.Tile.Graphics.IconSD.Id,
                T.EvtMatterReactorMalfunctionTitle,
                T.EvtMatterReactorMalfunctionDesc_V2.F(name, ending));

            Notification.BuildFor(randomNode.Entity, null, T.EvtMatterReactorMalfunctionTitle,
                    Consts.TicksPer4Hours, NotificationType.Critical)
                .Group("mr_malf")
                .Emergency(false)
                .WithVoice(VoiceLine.ReactorMalfunction)
                .Show();
            var text = Texts.LinkTo(T.EvtMatterReactorMalfunctionTitle,
                randomNode.Tile.PosIdx);
            s.Sig.ShowFanfare.Send(text);
            s.Sys.Log.AddLine(text);

            data.SetState(s, StoryEventState.Ending);
        }
    }
}
